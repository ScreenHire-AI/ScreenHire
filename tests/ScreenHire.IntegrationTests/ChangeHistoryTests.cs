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
/// organization's history is a 404; the table is append-only; and no secret ever reaches it.
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
        var next = first.At("nextBefore").GetInt64();
        var second = (await owner.GetAsync($"/v1/templates/{templateId}/history?limit=1&before={next}")).Expect(HttpStatusCode.OK);
        second.Text("entries.0.lines.0.text").Should().StartWith("renamed the stage");
        (await owner.GetAsync($"/v1/templates/{templateId}/history?limit=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("limit");

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
        var token = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Secret check", ["scopes"] = new JsonArray("Read", "Write"),
        })).Expect(HttpStatusCode.Created).Text("token");
        var endpoint = (await owner.PostAsync("/v1/webhooks", new JsonObject
        {
            ["url"] = receiver.Url.ToString(), ["events"] = new JsonArray("job.published"), ["description"] = "Secret check",
        })).Expect(HttpStatusCode.Created);
        var secret = endpoint.Text("signingSecret");
        var rotated = (await owner.PostAsync($"/v1/webhooks/{endpoint.Id("endpoint.id")}/rotate-secret")).Expect(HttpStatusCode.OK).Text("signingSecret");

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
        var records = await db.ChangeRecords.ToListAsync(Ct);
        records.Should().NotBeEmpty();
        records.Select(r => r.SubjectKind).Should().OnlyContain(k => k == ChangeSubjectKind.Job || k == ChangeSubjectKind.HiringTemplate || k == ChangeSubjectKind.EmailTemplate);
        var stored = JsonSerializer.Serialize(records.Select(r => r.Lines), JsonColumns.SerializerOptions);
        foreach (var value in new[] { token, secret, rotated }.Concat(hashes))
        {
            stored.Should().NotContain(value);
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
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction))
        {
            setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);
    }
}
