using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification;
using ScreenHire.Application.Privacy;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;
using Wolverine.Runtime.Recurring;
using static ScreenHire.IntegrationTests.CandidateTestSupport;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.DataRequests;

/// <summary>
/// Record retention (docs/architecture/data-requests.md, "Retention"): the setting and who may change it, the dry-run count,
/// and the daily run that deletes ended applications. Off by default. Each test makes its own organization; an application
/// is made old by moving the time it entered its stage, as the owner of the database.
/// </summary>
public sealed class RetentionTests(AppHostFixture fixture)
{
    private const string Path = "/v1/organization/data-retention";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retention_is_off_by_default_and_signed_out_callers_and_keys_get_401()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var anonymous = fixture.CreateHttpClient("api");
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Everything", ["Read", "Write"])).Text("token"));

        var retention = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);

        retention.At("months").ValueKind.Should().Be(JsonValueKind.Null);
        retention.At("lastRun").ValueKind.Should().Be(JsonValueKind.Null);
        retention.At("lastUpdated.by").GetString().Should().Be("ScreenHire", "nobody in the organization has changed the default");
        (await anonymous.GetAsync(Path, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync(Path, new { months = 12 }, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await key.GetAsync(Path)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await key.GetAsync($"{Path}/preview?months=12")).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_owners_and_admins_who_see_every_job_may_read_set_or_preview_retention()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        foreach (var member in new[] { recruiter, viewer })
        {
            (await member.GetAsync(Path)).Expect(HttpStatusCode.Forbidden);
            (await member.GetAsync($"{Path}/history")).Expect(HttpStatusCode.Forbidden);
            (await member.GetAsync($"{Path}/preview?months=12")).Expect(HttpStatusCode.Forbidden);
            (await member.PutAsync(Path, new JsonObject { ["months"] = 12 })).Expect(HttpStatusCode.Forbidden);
        }

        var roleId = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Compliance", ["copyFrom"] = "Viewer" })).Expect(HttpStatusCode.Created).Id();
        (await owner.PutAsync($"/v1/organization/roles/{roleId}", new JsonObject
        {
            ["name"] = "Compliance",
            ["permissions"] = new JsonArray("ViewHiring", "EraseCandidates"),
        })).Expect(HttpStatusCode.OK);
        using var narrow = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.GiveRoleAsync(narrow, roleId);
        (await narrow.GetAsync(Path)).Expect(HttpStatusCode.NotFound);
        (await narrow.GetAsync($"{Path}/history")).Expect(HttpStatusCode.NotFound);
        (await narrow.GetAsync($"{Path}/preview?months=12")).Expect(HttpStatusCode.NotFound);
        (await narrow.PutAsync(Path, new JsonObject { ["months"] = 12 })).Expect(HttpStatusCode.NotFound);

        (await admin.PutAsync(Path, new JsonObject { ["months"] = 12 })).Expect(HttpStatusCode.OK).At("months").GetInt32().Should().Be(12);
        (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK).At("months").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task A_period_is_1_to_120_months_or_null_and_each_edit_is_in_the_settings_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        foreach (var bad in new[] { 0, -1, 121, 5000 })
        {
            (await owner.PutAsync(Path, new JsonObject { ["months"] = bad })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("months");
        }

        (await owner.GetAsync(Path)).At("months").ValueKind.Should().Be(JsonValueKind.Null, "a refused save changes nothing");
        (await owner.PutAsync(Path, new JsonObject { ["months"] = 1 })).Expect(HttpStatusCode.OK);
        var set = (await owner.PutAsync(Path, new JsonObject { ["months"] = 120 })).Expect(HttpStatusCode.OK);
        set.At("months").GetInt32().Should().Be(120);
        set.At("lastUpdated.userId").GetGuid().Should().Be(owner.UserId);
        (await owner.PutAsync(Path, new JsonObject { ["months"] = 120 })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync(Path, new JsonObject { ["months"] = null })).Expect(HttpStatusCode.OK).At("months").ValueKind.Should().Be(JsonValueKind.Null);
        (await owner.PutAsync(Path, new JsonObject())).Expect(HttpStatusCode.OK).At("months").ValueKind.Should().Be(JsonValueKind.Null);

        var lines = (await owner.GetAsync($"{Path}/history")).Expect(HttpStatusCode.OK).At("entries").EnumerateArray()
            .Select(e => e.GetProperty("lines")[0].GetProperty("text").GetString()).ToList();
        lines.Should().Equal(
            "turned off deleting ended applications",
            "changed when ended applications are deleted to 120 months after they end",
            "turned on deleting ended applications 1 month after they end");
        (await owner.GetAsync($"{Path}/preview?months=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("months");
        (await owner.GetAsync($"{Path}/preview?months=121")).Expect(HttpStatusCode.BadRequest);
        (await owner.GetAsync($"{Path}/preview")).Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_preview_counts_what_the_run_removes_and_the_run_keeps_saved_candidates_live_applications_and_other_organizations()
    {
        using var world = await fixture.NewWorldAsync("Platform Engineer");
        using var elsewhere = await fixture.NewWorldAsync("Platform Engineer");
        var owner = world.Owner;
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");

        // A: one ended application a year old. The candidate is erased whole.
        var ended = await RejectedAsync(owner, world.ApplicationId, monthsAgo: 13);
        // B: an ended application and a live one. Only the ended one goes.
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var partlyEnded = (await owner.CreateApplicationAsync(world.JobId, person.Email, person.FullName)).Id();
        var live = (await owner.CreateApplicationAsync(secondJob, person.Email, person.FullName)).Id();
        var partCandidate = await CandidateOfAsync(owner, partlyEnded);
        await RejectedAsync(owner, partlyEnded, monthsAgo: 13);
        // C: a saved candidate whose application is as old as A's.
        var saved = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var savedApplication = await fixture.ApplyAsync(world.Slug, world.JobId, saved, Ct, keepForFutureRoles: true);
        (await owner.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = new JsonArray(JsonValue.Create(savedApplication)) })).Expect(HttpStatusCode.OK);
        await AgeAsync(savedApplication, monthsAgo: 13);
        // D: ended only two months ago.
        var recent = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var recentApplication = (await owner.CreateApplicationAsync(world.JobId, recent.Email, recent.FullName)).Id();
        await RejectedAsync(owner, recentApplication, monthsAgo: 2);
        // E: another organization, with an application as old as A's.
        var foreign = await RejectedAsync(elsewhere.Owner, elsewhere.ApplicationId, monthsAgo: 13);
        (await elsewhere.Owner.PutAsync(Path, new JsonObject { ["months"] = null })).Expect(HttpStatusCode.OK);

        var preview = (await owner.GetAsync($"{Path}/preview?months=6")).Expect(HttpStatusCode.OK);
        preview.At("applications").GetInt32().Should().Be(2, "the single ended application and the ended one of the partly ended person");
        preview.At("candidates").GetInt32().Should().Be(2);
        (await owner.GetAsync($"{Path}/preview?months=24")).At("applications").GetInt32().Should().Be(0);
        (await owner.GetAsync(Path)).At("months").ValueKind.Should().Be(JsonValueKind.Null, "a preview saves nothing");

        (await owner.PutAsync(Path, new JsonObject { ["months"] = 6 })).Expect(HttpStatusCode.OK);
        await RunAsync(world.OrganizationId);

        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/applications/{ended}")).Status == HttpStatusCode.NotFound
                && (await owner.GetAsync($"/v1/applications/{partlyEnded}")).Status == HttpStatusCode.NotFound,
            "the run to delete both ended applications", Ct);
        (await owner.GetAsync($"/v1/candidates/{world.CandidateId}")).Expect(HttpStatusCode.NotFound);
        var remaining = (await owner.GetAsync($"/v1/candidates/{partCandidate}")).Expect(HttpStatusCode.OK);
        remaining.At("applications").GetArrayLength().Should().Be(1);
        (await owner.GetAsync($"/v1/applications/{live}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/applications/{savedApplication}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/applications/{recentApplication}")).Expect(HttpStatusCode.OK);
        (await elsewhere.Owner.GetAsync($"/v1/applications/{foreign}")).Expect(HttpStatusCode.OK);

        var entries = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "RetentionApplied").ToList();
        entries.Should().ContainSingle();
        entries[0].GetProperty("itemCount").GetInt32().Should().Be(2);
        entries[0].GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);
        entries[0].GetRawText().Should().NotContain(person.Email).And.NotContain(person.FullName);
        var lastRun = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK).At("lastRun");
        lastRun.GetProperty("applicationsRemoved").GetInt32().Should().Be(2);
        lastRun.GetProperty("at").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        (await owner.GetAsync($"{Path}/preview?months=6")).At("applications").GetInt32().Should().Be(0, "a second run finds nothing");

        // A run with nothing to delete adds no entry.
        await RunAsync(world.OrganizationId);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await owner.GetAsync("/v1/organization/membership-log")).Body.EnumerateArray()
            .Count(e => e.GetProperty("kind").GetString() == "RetentionApplied").Should().Be(1);
    }

    [Fact]
    public async Task With_retention_off_the_run_deletes_nothing_even_where_another_organization_on_the_same_run_loses_applications()
    {
        using var off = await fixture.NewWorldAsync();
        using var on = await fixture.NewWorldAsync();
        var kept = await RejectedAsync(off.Owner, off.ApplicationId, monthsAgo: 30);
        var removed = await RejectedAsync(on.Owner, on.ApplicationId, monthsAgo: 30);
        (await on.Owner.PutAsync(Path, new JsonObject { ["months"] = 12 })).Expect(HttpStatusCode.OK);

        await RunAsync(off.OrganizationId);
        await RunAsync(on.OrganizationId);

        await WolverineStore.WaitUntilAsync(
            async () => (await on.Owner.GetAsync($"/v1/applications/{removed}")).Status == HttpStatusCode.NotFound,
            "the run to delete the organization's ended application", Ct);
        (await off.Owner.GetAsync($"/v1/applications/{kept}")).Expect(HttpStatusCode.OK);
        (await off.Owner.GetAsync($"{Path}")).At("lastRun").ValueKind.Should().Be(JsonValueKind.Null);
        (await off.Owner.GetAsync("/v1/organization/membership-log")).Body.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("kind").GetString() == "RetentionApplied");

        // Turned off again, the next run leaves the rest alone.
        (await on.Owner.PutAsync(Path, new JsonObject { ["months"] = null })).Expect(HttpStatusCode.OK);
        var later = await RejectedAsync(on.Owner, (await on.Owner.CreateApplicationAsync(on.JobId, ApiSession.NewEmail("late"), "Late Person")).Id(), monthsAgo: 30);
        await RunAsync(on.OrganizationId);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await on.Owner.GetAsync($"/v1/applications/{later}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_daily_sweeps_are_tracked_schedules()
    {
        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], Ct, withRecurringSchedules: true);
        var schedules = worker.Services.GetRequiredService<IRecurringScheduleControl>();

        await WolverineStore.WaitUntilAsync(
            async () => (await schedules.QueryAsync(Ct)).Any(s => s.Name == nameof(PurgeEndedApplications))
                && (await schedules.QueryAsync(Ct)).Any(s => s.Name == nameof(PurgeExpiredDataExports)),
            "the retention and data export schedules to be tracked", Ct);
        var all = await schedules.QueryAsync(Ct);
        var retention = all.Should().ContainSingle(s => s.Name == nameof(PurgeEndedApplications)).Subject;
        retention.CronExpression.Should().Be("12 4 * * *");
        retention.Paused.Should().BeFalse();
        var exports = all.Should().ContainSingle(s => s.Name == nameof(PurgeExpiredDataExports)).Subject;
        exports.CronExpression.Should().Be("47 3 * * *");
        exports.Paused.Should().BeFalse();
    }

    // Moves the application to Rejected and makes it end that long ago.
    private async Task<Guid> RejectedAsync(Tenant owner, Guid applicationId, int monthsAgo)
    {
        (await owner.PostAsync($"/v1/applications/{applicationId}/move", new JsonObject { ["toStageId"] = "rejected" })).Expect(HttpStatusCode.OK);
        await AgeAsync(applicationId, monthsAgo);
        return applicationId;
    }

    private Task AgeAsync(Guid applicationId, int monthsAgo) =>
        fixture.OwnerSqlAsync(
            "UPDATE applications SET stage_entered_at = now() - make_interval(months => @months), kept_in_mind_at = CASE WHEN kept_in_mind_at IS NULL THEN NULL ELSE now() - make_interval(months => @months) END WHERE id = @id",
            ("id", applicationId), ("months", monthsAgo));

    // What the daily sweep sends for one organization.
    private async Task RunAsync(Guid organizationId)
    {
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new PurgeOrganizationEndedApplications(organizationId, DateTimeOffset.UtcNow));
    }
}
