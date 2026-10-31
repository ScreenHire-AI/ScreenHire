using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.TalentPool;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.Infrastructure.Tenancy;
using Wolverine;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// The permission period (docs/architecture/talent-pool.md): the pool leaves out whoever's permission has ended before
/// the daily sweep runs, and the sweep, in the running worker, takes their entry out and records why.
/// </summary>
public sealed class TalentPoolExpiryTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_permission_older_than_twelve_months_drops_out_of_the_list_at_once_and_the_sweep_records_that_it_ran_out()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Expiry job");
        var expired = await world.KeptPersonAsync(job, "expired");
        var current = await world.KeptPersonAsync(job, "current");
        await BackdateConsentAsync(world, expired.ApplicationId, DateTimeOffset.UtcNow.AddMonths(-13));

        (await world.PoolCandidateIdsAsync()).Should().Equal([current.CandidateId], "the list leaves out an ended permission before any sweep");
        (await world.DetailAsync(expired.ApplicationId)).At("inTalentPool").GetBoolean().Should().BeTrue("the entry stays until the sweep removes it");

        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new ExpireOrganizationTalentPool(world.Owner.OrganizationId, DateTimeOffset.UtcNow));
            await WolverineStore.WaitUntilAsync(
                async () => !(await world.DetailAsync(expired.ApplicationId)).At("inTalentPool").GetBoolean(),
                "the worker's sweep to take the expired entry out",
                Ct);
        }

        var left = await world.EntryAsync(expired.ApplicationId, "LeftTalentPool");
        left.GetProperty("poolExit").GetString().Should().Be("Expired");
        left.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "nobody on the team did it");
        (await world.DetailAsync(current.ApplicationId)).At("inTalentPool").GetBoolean().Should().BeTrue("a permission that still holds stays");
        (await world.TimelineAsync(current.ApplicationId)).Select(e => e.GetProperty("type").GetString()).Should().NotContain("LeftTalentPool");
    }

    [Fact]
    public async Task Shortening_the_period_ends_permissions_already_given_but_lengthening_it_never_extends_them()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Period job");
        var sevenMonthsOld = await world.KeptPersonAsync(job, "seven");
        var thirteenMonthsOld = await world.KeptPersonAsync(job, "thirteen");
        await BackdateConsentAsync(world, sevenMonthsOld.ApplicationId, DateTimeOffset.UtcNow.AddMonths(-7));
        await BackdateConsentAsync(world, thirteenMonthsOld.ApplicationId, DateTimeOffset.UtcNow.AddMonths(-13));
        (await world.PoolCandidateIdsAsync()).Should().Equal([sevenMonthsOld.CandidateId], "13 months is past the 12 the form promised");

        (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 24 })).Expect(HttpStatusCode.OK);
        (await world.PoolCandidateIdsAsync()).Should().Equal([sevenMonthsOld.CandidateId], "a longer setting does not revive a permission given for 12 months");

        (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 6 })).Expect(HttpStatusCode.OK);
        (await world.PoolAsync()).Should().BeEmpty("a shorter setting applies to every permission at once");
    }

    // The application's setters are private, so the consent's date is moved with a set-based update, as the application role
    // and the organization as tenant, which row-level security still checks.
    private async Task BackdateConsentAsync(PoolWorld world, Guid applicationId, DateTimeOffset givenAt)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(world.Owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var changed = await db.Applications.Where(a => a.Id == applicationId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.FutureRolesConsentGivenAt, (DateTimeOffset?)givenAt), Ct);
        changed.Should().Be(1);
    }
}

/// <summary>The organization's talent pool settings: the permission period, its range, its history and who may change it.</summary>
public sealed class TalentPoolSettingsTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_period_defaults_to_twelve_months_and_a_change_is_recorded_in_the_history()
    {
        using var world = await PoolWorld.CreateAsync(fixture);

        var initial = (await world.Owner.GetAsync("/v1/organization/talent-pool-settings")).Expect(HttpStatusCode.OK);
        initial.At("permissionMonths").GetInt32().Should().Be(12);
        initial.At("minMonths").GetInt32().Should().Be(6);
        initial.At("maxMonths").GetInt32().Should().Be(24);
        initial.Text("lastUpdated.by").Should().Be("ScreenHire");
        (await world.Owner.GetAsync("/v1/organization/talent-pool-settings/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);

        var updated = (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 18 })).Expect(HttpStatusCode.OK);

        updated.At("permissionMonths").GetInt32().Should().Be(18);
        updated.Text("lastUpdated.by").Should().Be("Member");
        updated.Id("lastUpdated.userId").Should().Be(world.Owner.UserId);
        (await world.Owner.GetAsync("/v1/organization/talent-pool-settings")).Expect(HttpStatusCode.OK).At("permissionMonths").GetInt32().Should().Be(18);
        var history = (await world.Owner.GetAsync("/v1/organization/talent-pool-settings/history")).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(1);
        history.Id("entries.0.userId").Should().Be(world.Owner.UserId);
        history.Text("entries.0.lines.0.text").Should().Contain("12 months");
    }

    [Fact]
    public async Task The_period_the_form_shows_and_the_permission_it_stores_follow_the_setting()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, jobSlug) = await world.OpenJobAsync("Setting job");
        var formPath = $"/v1/public/organizations/{world.Slug}/jobs/{jobSlug}/application-form";
        (await world.Careers.GetFromJsonAsync<JsonElement>(formPath, Ct)).At("talentPoolMonths").GetInt32().Should().Be(12);

        (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 18 })).Expect(HttpStatusCode.OK);

        (await world.Careers.GetFromJsonAsync<JsonElement>(formPath, Ct)).At("talentPoolMonths").GetInt32().Should().Be(18);
        var detail = await world.DetailAsync(await world.ApplyAsync(job, ApiSession.NewEmail("eighteen"), keep: true));
        detail.At("futureRolesUntil").GetDateTimeOffset().Should().Be(detail.At("futureRolesGivenAt").GetDateTimeOffset().AddMonths(18));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(0)]
    public async Task A_period_outside_six_to_twenty_four_months_is_refused_and_nothing_changes(int months)
    {
        using var world = await PoolWorld.CreateAsync(fixture);

        var refused = (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = months }))
            .Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Equal("permissionMonths");
        (await world.Owner.GetAsync("/v1/organization/talent-pool-settings")).Expect(HttpStatusCode.OK).At("permissionMonths").GetInt32().Should().Be(12);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(24)]
    public async Task The_limits_of_the_range_are_allowed(int months)
    {
        using var world = await PoolWorld.CreateAsync(fixture);

        (await world.Owner.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = months }))
            .Expect(HttpStatusCode.OK).At("permissionMonths").GetInt32().Should().Be(months);
    }

    [Fact]
    public async Task A_recruiter_can_read_the_period_but_not_change_it()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        using var recruiter = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter");

        (await recruiter.PutAsync("/v1/organization/talent-pool-settings", new JsonObject { ["permissionMonths"] = 18 })).Status
            .Should().Be(HttpStatusCode.Forbidden);

        (await recruiter.GetAsync("/v1/organization/talent-pool-settings")).Expect(HttpStatusCode.OK).At("permissionMonths").GetInt32().Should().Be(12);
    }
}
