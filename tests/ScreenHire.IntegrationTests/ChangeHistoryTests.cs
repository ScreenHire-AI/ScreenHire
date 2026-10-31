using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Change history through the running API (docs/architecture/change-history.md): an edit of a tracked object records
/// who made it, when and what changed, in the same save; each object's history pages newest first; another
/// organization's history is a 404; the table is append-only; and no secret ever reaches it. The Team and API keys pages
/// have one history each, and an object nothing has changed since it was created says Created.
/// </summary>
public sealed class ChangeHistoryTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_email_template_edit_records_who_when_and_what_and_restoring_is_recorded_too()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var original = (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}")).Expect(HttpStatusCode.OK);
        original.At("lastUpdated.by").GetString().Should().Be("ScreenHire", "nobody has changed the built-in yet");
        original.At("lastUpdated.at").ValueKind.Should().Be(JsonValueKind.Null);
        var subject = original.Text("subject");

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        (await owner.PutAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}", new JsonObject
        {
            ["subject"] = "Next steps for your application for {{ jobTitle }}",
            ["body"] = original.Text("body"),
        })).Expect(HttpStatusCode.OK);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var history = (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}/history")).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(1);
        history.Text("entries.0.by").Should().Be("Member");
        history.Id("entries.0.userId").Should().Be(owner.UserId);
        history.At("entries.0.at").GetDateTimeOffset().Should().BeAfter(before).And.BeBefore(after);
        history.Text("entries.0.lines.0.text").Should().Be("changed the subject");
        history.Text("entries.0.lines.0.details.0.before").Should().Be(subject);
        history.Text("entries.0.lines.0.details.0.after").Should().Be("Next steps for your application for {{ jobTitle }}");

        var read = (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}")).Expect(HttpStatusCode.OK);
        read.Text("lastUpdated.by").Should().Be("Member");
        read.Id("lastUpdated.userId").Should().Be(owner.UserId);

        (await owner.DeleteAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}")).Expect(HttpStatusCode.NoContent);
        var restored = (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}/history")).Expect(HttpStatusCode.OK);
        restored.Count("entries").Should().Be(2);
        restored.Text("entries.0.lines.0.text").Should().Be("restored the original wording");
    }

    [Fact]
    public async Task Each_settings_page_has_its_own_history_and_its_last_update()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var name = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("name");
        (await owner.GetAsync("/v1/organization/branding")).Expect(HttpStatusCode.OK).Text("lastUpdated.by").Should().Be("ScreenHire", "the default colour");
        (await owner.GetAsync("/v1/organization/verification-settings")).Expect(HttpStatusCode.OK).Text("lastUpdated.by").Should().Be("ScreenHire", "the defaults");

        (await owner.PutAsync("/v1/organization", new JsonObject { ["name"] = $"{name} Studio" })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync("/v1/organization/branding", new JsonObject { ["brandColor"] = "#1f4e5a" })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync("/v1/organization/verification-settings", new JsonObject { ["llmConsistencyCheck"] = true, ["monthlyTokenBudget"] = 250000 }))
            .Expect(HttpStatusCode.OK);

        var renamed = (await owner.GetAsync("/v1/organization/history")).Expect(HttpStatusCode.OK);
        renamed.Count("entries").Should().Be(1, "the brand colour is on the same row but has its own history");
        renamed.Text("entries.0.lines.0.text").Should().Be($"renamed the organization from {name} to {name} Studio");
        var branding = (await owner.GetAsync("/v1/organization/branding/history")).Expect(HttpStatusCode.OK);
        branding.Count("entries").Should().Be(1);
        branding.Text("entries.0.lines.0.text").Should().Be("set the brand colour to #1f4e5a");
        var verification = (await owner.GetAsync("/v1/organization/verification-settings/history")).Expect(HttpStatusCode.OK);
        verification.Text("entries.0.lines.0.text").Should().Be("turned on the consistency check");
        verification.Text("entries.0.lines.1.text").Should().Be("set the monthly token budget to 250,000");

        foreach (var path in new[] { "/v1/organization", "/v1/organization/branding", "/v1/organization/verification-settings" })
        {
            var read = (await owner.GetAsync(path)).Expect(HttpStatusCode.OK);
            read.Text("lastUpdated.by").Should().Be("Member", path);
            read.Id("lastUpdated.userId").Should().Be(owner.UserId, path);
        }

        (await other.GetAsync("/v1/organization/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0, "each organization reads its own");
    }

    [Fact]
    public async Task A_template_copy_a_draft_edit_and_a_publish_are_three_entries_newest_first_that_page()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var created = (await owner.PostAsync("/v1/templates", new JsonObject
        {
            ["name"] = "Technical interview",
            ["copyFromVersionId"] = SystemTemplates.SoftwareEngineerVersion4.ToString(),
        })).Expect(HttpStatusCode.Created);
        var templateId = created.Id();
        created.Text("lastUpdated.by").Should().Be("Member");

        var definition = await Samples.DefinitionOfAsync(owner, templateId, 1);
        var stage = definition["workflow"]!["stages"]!.AsArray()[1]!;
        var stageName = stage["name"]!.GetValue<string>();
        (await owner.PutAsync($"/v1/templates/{templateId}/versions/1", new JsonObject
        {
            ["definition"] = Samples.DefinitionWithRenamedStage(definition, stage["id"]!.GetValue<string>(), "Phone screen"),
        })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/templates/{templateId}/versions/1/publish")).Expect(HttpStatusCode.OK);

        var history = (await owner.GetAsync($"/v1/templates/{templateId}/history")).Expect(HttpStatusCode.OK);
        history.Body.GetProperty("entries").EnumerateArray().Select(e => e.At("lines.0.text").GetString()).Should().Equal(
            "published version 1",
            $"renamed the stage from {stageName} to Phone screen",
            "created the template from Software engineer");
        history.At("nextBefore").ValueKind.Should().Be(JsonValueKind.Null);

        var first = (await owner.GetAsync($"/v1/templates/{templateId}/history?limit=1")).Expect(HttpStatusCode.OK);
        first.Count("entries").Should().Be(1);
        var next = first.At("nextBefore").GetGuid();
        next.Should().Be(first.Id("entries.0.id"), "the cursor is the last entry's id");
        var second = (await owner.GetAsync($"/v1/templates/{templateId}/history?limit=1&before={next}")).Expect(HttpStatusCode.OK);
        second.Text("entries.0.lines.0.text").Should().StartWith("renamed the stage");
        (await owner.GetAsync($"/v1/templates/{templateId}/history?limit=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("limit");
        (await owner.GetAsync($"/v1/templates/{templateId}/history?before={Guid.CreateVersion7()}"))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("before");

        var system = (await owner.GetAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}")).Expect(HttpStatusCode.OK);
        system.Text("lastUpdated.by").Should().Be("ScreenHire");
    }

    [Fact]
    public async Task A_job_edit_with_an_api_key_records_the_key_and_no_person()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        var jobId = job.Id();
        var key = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Sync",
            ["scopes"] = new JsonArray("Read", "Write"),
        })).Expect(HttpStatusCode.Created);

        using (var client = KeyClient(key.Text("token")))
        {
            using var response = await client.PutAsJsonAsync($"/v1/jobs/{jobId}", Samples.RemoteJob("Senior Data Engineer"), Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var history = (await owner.GetAsync($"/v1/jobs/{jobId}/history")).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(2);
        history.Text("entries.0.by").Should().Be("ApiKey");
        history.Id("entries.0.apiKeyId").Should().Be(key.Id("key.id"));
        history.At("entries.0.userId").ValueKind.Should().Be(JsonValueKind.Null);
        history.Text("entries.0.lines.0.text").Should().Be("renamed the job from Data Engineer to Senior Data Engineer");
        history.Text("entries.1.by").Should().Be("Member");
        history.Text("entries.1.lines.0.text").Should().Be("created the job");

        var read = (await owner.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);
        read.Text("lastUpdated.by").Should().Be("ApiKey");
        var list = (await owner.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK);
        list.Text("0.lastUpdated.by").Should().Be("ApiKey");
    }

    [Fact]
    public async Task Another_organization_gets_404_for_every_history_and_sees_none_of_its_entries()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var job = (await theirs.CreateJobAsync(Samples.RemoteJob("Their job"))).Id();
        var template = (await theirs.CreateTemplateAsync("Their template")).Id();
        var email = (await theirs.PostAsync("/v1/email-templates", new JsonObject
        {
            ["name"] = "Their email", ["subject"] = "Hello", ["body"] = "Hi {{ candidateName }}",
        })).Expect(HttpStatusCode.Created).Text("id");
        (await theirs.PutAsync($"/v1/email-templates/{EmailTemplateIds.ApplicationReceived}", new JsonObject
        {
            ["subject"] = "We have your application", ["body"] = "Thanks.",
        })).Expect(HttpStatusCode.OK);
        (await theirs.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Their key", ["scopes"] = new JsonArray("Read"),
        })).Expect(HttpStatusCode.Created);
        (await theirs.PostAsync("/v1/organization/invitations", new JsonObject
        {
            ["email"] = ApiSession.NewEmail("their-invitee"), ["role"] = "Viewer",
        })).Expect(HttpStatusCode.Created);

        (await ours.GetAsync("/v1/organization/api-keys/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);
        var ourKeys = (await ours.GetAsync("/v1/organization/api-keys/last-update")).Expect(HttpStatusCode.OK);
        ourKeys.At("created").GetBoolean().Should().BeTrue("their key is not a change to ours");
        ourKeys.At("by").ValueKind.Should().Be(JsonValueKind.Null);
        var ourTeam = (await ours.GetAsync("/v1/organization/team/history")).Expect(HttpStatusCode.OK);
        ourTeam.Count("entries").Should().Be(1, "only our own creation, not their invitation");
        ourTeam.Id("entries.0.userId").Should().Be(ours.UserId);

        (await ours.GetAsync($"/v1/jobs/{job}/history")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync($"/v1/templates/{template}/history")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync($"/v1/email-templates/{email}/history")).Expect(HttpStatusCode.NotFound);
        (await ours.GetAsync($"/v1/email-templates/{EmailTemplateIds.ApplicationReceived}/history"))
            .Expect(HttpStatusCode.OK).Count("entries").Should().Be(0, "their edit of the built-in is theirs");
        (await theirs.GetAsync($"/v1/email-templates/{EmailTemplateIds.ApplicationReceived}/history"))
            .Expect(HttpStatusCode.OK).Count("entries").Should().Be(1);
    }

    [Fact]
    public async Task A_secret_is_never_stored_in_the_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var receiver = await WebhookReceiver.StartAsync(Ct);
        var created = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Secret check", ["scopes"] = new JsonArray("Read", "Write"),
        })).Expect(HttpStatusCode.Created);
        var token = created.Text("token");
        var prefix = created.Text("key.prefix");
        (await owner.PostAsync($"/v1/organization/api-keys/{created.Id("key.id")}/revoke")).Expect(HttpStatusCode.OK);
        var endpoint = (await owner.PostAsync("/v1/webhooks", new JsonObject
        {
            ["url"] = receiver.Url.ToString(), ["events"] = new JsonArray("job.published"), ["description"] = "Secret check",
        })).Expect(HttpStatusCode.Created);
        var secret = endpoint.Text("signingSecret");
        var rotated = (await owner.PostAsync($"/v1/webhooks/{endpoint.Id("endpoint.id")}/rotate-secret")).Expect(HttpStatusCode.OK).Text("signingSecret");

        // An invitation sent, one replaced by a second to the same address, and one revoked: three token hashes.
        var invitee = ApiSession.NewEmail("secret-check");
        (await owner.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = invitee, ["role"] = "Viewer" })).Expect(HttpStatusCode.Created);
        var invitationToken = await fixture.Mailpit.WaitForInvitationTokenAsync(invitee, Ct);
        var replacing = (await owner.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = invitee, ["role"] = "Recruiter" }))
            .Expect(HttpStatusCode.Created);
        (await owner.DeleteAsync($"/v1/organization/invitations/{replacing.Id()}")).Expect(HttpStatusCode.NoContent);

        var job = await owner.CreateJobAsync(Samples.RemoteJob("Secret check"));
        await owner.PublishJobAsync(job.Id());
        await owner.CreateTemplateAsync("Secret check");
        (await owner.PutAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}", new JsonObject
        {
            ["subject"] = "An update for {{ jobTitle }}", ["body"] = "Hi {{ candidateName }}",
        })).Expect(HttpStatusCode.OK);

        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var hashes = await db.ApiKeys.Select(k => k.SecretHash).ToListAsync(Ct);
        var tokenHashes = await db.OrganizationInvitations.Select(i => i.TokenHash).ToListAsync(Ct);
        tokenHashes.Should().HaveCount(2);
        var records = await db.ChangeRecords.ToListAsync(Ct);
        records.Select(r => r.SubjectKind).Should().OnlyContain(k =>
            k == ChangeSubjectKind.Job || k == ChangeSubjectKind.HiringTemplate || k == ChangeSubjectKind.EmailTemplate
            || k == ChangeSubjectKind.Team || k == ChangeSubjectKind.ApiKeys);
        records.Select(r => r.SubjectKind).Should().Contain([ChangeSubjectKind.Team, ChangeSubjectKind.ApiKeys]);
        var stored = JsonSerializer.Serialize(records.Select(r => r.Lines), JsonColumns.SerializerOptions);
        var served = (await owner.GetAsync("/v1/organization/api-keys/history")).Expect(HttpStatusCode.OK).Body.GetRawText()
            + (await owner.GetAsync("/v1/organization/team/history")).Expect(HttpStatusCode.OK).Body.GetRawText();
        foreach (var value in new[] { token, prefix, secret, rotated, invitationToken }.Concat(hashes).Concat(tokenHashes))
        {
            stored.Should().NotContain(value);
            served.Should().NotContain(value);
        }
    }

    [Fact]
    public async Task The_history_can_be_added_to_but_never_changed_or_deleted()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await owner.CreateJobAsync(Samples.RemoteJob("Append only"));

        var update = () => ExecuteAsync(owner.OrganizationId, "UPDATE change_records SET subject_id = 'x'");
        var delete = () => ExecuteAsync(owner.OrganizationId, "DELETE FROM change_records");

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_team_page_records_invitations_joins_role_and_job_access_changes_and_removals()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var started = (await owner.GetAsync("/v1/organization/team/last-update")).Expect(HttpStatusCode.OK);
        started.At("created").GetBoolean().Should().BeTrue("nothing has changed the team since onboarding created it");
        started.Text("by").Should().Be("Member");
        started.Id("userId").Should().Be(owner.UserId);

        var revokedAddress = ApiSession.NewEmail("revoked");
        var revoked = (await owner.PostAsync("/v1/organization/invitations", new JsonObject { ["email"] = revokedAddress, ["role"] = "Recruiter" }))
            .Expect(HttpStatusCode.Created);
        (await owner.DeleteAsync($"/v1/organization/invitations/{revoked.Id()}")).Expect(HttpStatusCode.NoContent);
        var joinerAddress = ApiSession.NewEmail("joiner");
        using var joiner = await fixture.InviteAndJoinAsync(owner, "Viewer", joinerAddress);
        var memberId = await owner.MemberIdAsync(joiner.UserId);
        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Recruiter" })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync($"/v1/organization/members/{memberId}/job-access", new JsonObject { ["jobAccess"] = "HiringTeamOnly" }))
            .Expect(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/v1/organization/members/{memberId}")).Expect(HttpStatusCode.NoContent);

        var history = (await owner.GetAsync("/v1/organization/team/history")).Expect(HttpStatusCode.OK);
        var entries = history.Body.GetProperty("entries").EnumerateArray().ToList();
        entries.Select(e => e.At("lines.0.text").GetString()).Should().Equal(
            $"removed {joinerAddress} from the team",
            $"changed {joinerAddress}'s jobs from All jobs to Only jobs they're on",
            $"changed {joinerAddress}'s role from Viewer to Recruiter",
            "joined the team as Viewer",
            $"invited {joinerAddress} as Viewer",
            $"revoked the invitation to {revokedAddress}",
            $"invited {revokedAddress} as Recruiter",
            "created the team");
        entries[3].At("userId").GetGuid().Should().Be(joiner.UserId, "the person who accepted joined");
        entries.Where((_, i) => i != 3).Should().OnlyContain(e => e.At("userId").GetGuid() == owner.UserId);
        entries[0].At("lines.0.parts.1.strong").GetBoolean().Should().BeTrue("the member is the thing that changed");
        entries[2].At("lines.0.details.0.before").GetString().Should().Be("Viewer");

        var last = (await owner.GetAsync("/v1/organization/team/last-update")).Expect(HttpStatusCode.OK);
        last.At("created").GetBoolean().Should().BeFalse();
        last.Id("userId").Should().Be(owner.UserId);
        (await other.GetAsync("/v1/organization/team/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(1, "only its own creation");

        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        (await viewer.GetAsync("/v1/organization/team/history")).Expect(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/v1/organization/team/last-update")).Expect(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_api_keys_page_records_who_created_and_revoked_each_key_with_its_name_and_access()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var untouched = (await owner.GetAsync("/v1/organization/api-keys/last-update")).Expect(HttpStatusCode.OK);
        untouched.At("created").GetBoolean().Should().BeTrue("the organization began with no keys");
        untouched.At("by").ValueKind.Should().Be(JsonValueKind.Null);
        (await owner.GetAsync("/v1/organization/api-keys/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);

        var key = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Payroll sync", ["scopes"] = new JsonArray("Read", "Write"),
        })).Expect(HttpStatusCode.Created);
        using (var client = KeyClient(key.Text("token")))
        {
            using var response = await client.GetAsync("/v1/jobs", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await owner.PostAsync($"/v1/organization/api-keys/{key.Id("key.id")}/revoke")).Expect(HttpStatusCode.OK);

        var history = (await owner.GetAsync("/v1/organization/api-keys/history")).Expect(HttpStatusCode.OK);
        history.Body.GetProperty("entries").EnumerateArray().Select(e => e.At("lines.0.text").GetString())
            .Should().Equal(["revoked the key Payroll sync", "created the key Payroll sync"], "using a key records nothing");
        history.Text("entries.1.lines.0.details.0.label").Should().Be("Access");
        history.Text("entries.1.lines.0.details.0.after").Should().Be("Read and write");
        history.Body.GetProperty("entries").EnumerateArray().Should().OnlyContain(e => e.At("userId").GetGuid() == owner.UserId);

        var last = (await owner.GetAsync("/v1/organization/api-keys/last-update")).Expect(HttpStatusCode.OK);
        last.Text("by").Should().Be("Member");
        last.At("created").GetBoolean().Should().BeFalse("adding a key changes the page; it does not create it");

        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        (await viewer.GetAsync("/v1/organization/api-keys/history")).Expect(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/v1/organization/api-keys/last-update")).Expect(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_object_nothing_has_changed_since_it_was_created_says_created_until_its_first_change()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        job.At("lastUpdated.created").GetBoolean().Should().BeTrue();
        job.Id("lastUpdated.userId").Should().Be(owner.UserId);
        var template = await owner.CreateTemplateAsync("Technical interview");
        template.At("lastUpdated.created").GetBoolean().Should().BeTrue();

        (await owner.PutAsync($"/v1/jobs/{job.Id()}", Samples.RemoteJob("Senior Data Engineer"))).Expect(HttpStatusCode.OK);
        var edited = (await owner.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
        edited.At("lastUpdated.created").GetBoolean().Should().BeFalse();
        edited.Text("lastUpdated.by").Should().Be("Member");

        var organization = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK);
        organization.At("lastUpdated.created").GetBoolean().Should().BeTrue("nobody has renamed it");
        organization.At("lastUpdated.by").ValueKind.Should().Be(JsonValueKind.Null, "creating the organization is not a change on its page");

        (await owner.PutAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}", new JsonObject
        {
            ["subject"] = "An update for {{ jobTitle }}", ["body"] = "Hi {{ candidateName }}",
        })).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/email-templates/{EmailTemplateIds.StageChange}")).Expect(HttpStatusCode.OK)
            .At("lastUpdated.created").GetBoolean().Should().BeFalse("a built-in's first edit is an edit of ScreenHire's wording");
    }

    // An integrator's client: the key in the Authorization header, no cookies and no antiforgery header.
    private HttpClient KeyClient(string token)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = fixture.GetEndpoint("api") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // A statement on a raw screenhire_app connection with the tenant set transaction-locally.
    private async Task ExecuteAsync(Guid organizationId, string sql)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true), set_config('app.job_scope', 'all', true)", connection, transaction))
        {
            setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);
    }
}
