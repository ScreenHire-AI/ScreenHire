using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The Reports page's numbers (docs/architecture/reports.md): one organization with a known history, where each rule has
/// a case that tells it apart. The API's clock is the real one, so history sits days away from the window's edges and
/// every request names its own range.
/// </summary>
public sealed class ReportsTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset DaysAgo(double days) => Now.AddDays(-days);

    // The last 30 days, with a day to spare at the end.
    private static readonly string Range = $"from={Uri.EscapeDataString(DaysAgo(30).ToString("O"))}&to={Uri.EscapeDataString(Now.AddDays(1).ToString("O"))}";

    // Days before now that the seeded applications arrived in the range, and that the five hires in it were made.
    private static readonly int[] AppliedDaysAgo = [10, 12, 6, 20, 15, 14, 25];
    private static readonly int[] HiredDaysAgo = [5, 15, 10, 9, 11];

    // A pipeline with an assessment and a verification step, beside the default one.
    private static readonly HiringWorkflow Assessed = HiringWorkflow.Default with
    {
        Stages =
        [
            new("applied", "Applied", StageKind.Applied),
            new("screening", "Screening", StageKind.Screening),
            new("technical", "Technical exercise", StageKind.Assessment),
            new("id-check", "ID check", StageKind.Verification),
            new("interview", "Interview", StageKind.Interview),
            new("offer", "Offer", StageKind.Offer),
            new("hired", "Hired", StageKind.Hired),
            new("rejected", "Rejected", StageKind.Rejected),
            new("withdrawn", "Withdrawn", StageKind.Withdrawn),
        ],
    };

    [Fact]
    public async Task The_funnel_time_to_hire_and_sources_count_a_known_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (platform, data) = await SeedAsync(owner.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        // Seven applications arrived in the range; the skip, the rejection, the move back out of Hired and the pass
        // through verification each count at the furthest row they reached.
        Funnel(report).Should().Equal(
            ("Applied", 7, 6), ("Screening", 6, 6), ("Assessment", 6, 5), ("Interview", 5, 4), ("Offer", 4, 3), ("Hired", 3, null));
        report.At("funnel").EnumerateArray().Select(s => s.GetProperty("kind").GetString())
            .Should().NotContain("Verification", "a verification step is never a row (ADR-0004)");

        // Hires made in the range, whenever they applied: 5, 30 and 45 days on Platform, 5 and 14 on Data.
        report.At("headline.hires").GetInt32().Should().Be(5);
        report.At("headline.medianDaysToHire").GetDouble().Should().BeApproximately(14, 0.01, "the median of every hire, not of the jobs' medians");
        Jobs(report).Should().BeEquivalentTo([(platform, 3, 30.0), (data, 2, 9.5)]);

        // A recruiter's first, then the organization's own site, then a job board's apply link, else the careers page.
        Sources(report).Should().Equal(("careers", 3, 0), ("recruiter", 2, 1), ("board:talent", 1, 1), ("website", 1, 1));
    }

    [Fact]
    public async Task The_headline_agrees_with_the_funnel_the_weeks_and_the_jobs()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(owner.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        var funnel = Funnel(report).ToDictionary(row => row.Item1, row => row.Item2);
        report.At("headline.applications").GetInt32().Should().Be(funnel["Applied"]);
        report.At("headline.reachedInterview").GetInt32().Should().Be(funnel["Interview"]);
        report.At("headline.offers").GetInt32().Should().Be(funnel["Offer"]);

        // Hires are the ones made in the range, however they are cut: the card, the weeks and the jobs say one number.
        var hires = report.At("headline.hires").GetInt32();
        hires.Should().Be(5);
        report.At("weekly").EnumerateArray().Sum(w => w.GetProperty("hires").GetInt32()).Should().Be(hires);
        report.At("weekly").EnumerateArray().Sum(w => w.GetProperty("applications").GetInt32()).Should().Be(funnel["Applied"]);
        Jobs(report).Sum(job => job.Item2).Should().Be(hires);
        report.At("previous").ValueKind.Should().Be(JsonValueKind.Null, "no previous range was asked for");
    }

    [Fact]
    public async Task The_previous_range_gets_the_same_headline()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(owner.OrganizationId);
        var previous = $"previousFrom={Uri.EscapeDataString(DaysAgo(65).ToString("O"))}&previousTo={Uri.EscapeDataString(DaysAgo(30).ToString("O"))}";

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}&{previous}")).Expect(HttpStatusCode.OK);

        // Two applications arrived 60 and 40 days ago and were hired later, so both count at every row up to Hired, but
        // no hire was made in that window.
        report.At("previous.applications").GetInt32().Should().Be(2);
        report.At("previous.reachedInterview").GetInt32().Should().Be(2);
        report.At("previous.offers").GetInt32().Should().Be(2);
        report.At("previous.hires").GetInt32().Should().Be(0);
        report.At("previous.medianDaysToHire").ValueKind.Should().Be(JsonValueKind.Null);
        report.At("headline.applications").GetInt32().Should().Be(7, "the range itself does not change");
    }

    [Fact]
    public async Task Applications_and_hires_land_in_the_monday_weeks_of_the_persons_own_zone()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(owner.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}&timeZone=America/New_York")).Expect(HttpStatusCode.OK);

        var weeks = report.At("weekly").EnumerateArray()
            .Select(w => (Start: DateOnly.Parse(w.GetProperty("weekStart").GetString()!, System.Globalization.CultureInfo.InvariantCulture), Applications: w.GetProperty("applications").GetInt32(), Hires: w.GetProperty("hires").GetInt32()))
            .ToList();
        weeks.Should().OnlyContain(w => w.Start.DayOfWeek == DayOfWeek.Monday);
        weeks.Select(w => w.Start).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        weeks.Zip(weeks.Skip(1), (a, b) => b.Start.DayNumber - a.Start.DayNumber).Should().OnlyContain(gap => gap == 7, "weeks with nothing in them are still there");

        // Each application and hire sits in the week it happened in, counted in New York.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        static DateOnly Monday(DateTimeOffset at, TimeZoneInfo zone)
        {
            var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
            return local.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        }

        var applied = AppliedDaysAgo.GroupBy(d => Monday(DaysAgo(d), zone)).ToDictionary(g => g.Key, g => g.Count());
        var hired = HiredDaysAgo.GroupBy(d => Monday(DaysAgo(d), zone)).ToDictionary(g => g.Key, g => g.Count());
        weeks.Where(w => w.Applications > 0).ToDictionary(w => w.Start, w => w.Applications).Should().BeEquivalentTo(applied);
        weeks.Where(w => w.Hires > 0).ToDictionary(w => w.Start, w => w.Hires).Should().BeEquivalentTo(hired);
    }

    [Fact]
    public async Task Median_days_in_each_stage_come_from_the_hires_made_in_the_range_and_skip_verification()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(owner.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        // Per hire, days in each kind: Applied 1, 45, 9.5, 1, 1; Screening 1, 1; Assessment 1; Interview 1, 20.5, 1, 1;
        // Offer 2, 2, 10. The ID check on Data is a verification step and has no row (ADR-0004).
        Stages(report).Should().Equal(("Applied", 1.0, 5), ("Screening", 1.0, 2), ("Assessment", 1.0, 1), ("Interview", 1.0, 4), ("Offer", 2.0, 3));
    }

    [Fact]
    public async Task Time_in_a_stage_removed_since_counts_for_no_stage()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = TestData.NewJob(owner.OrganizationId, $"revised-{Guid.NewGuid():N}", "Revised Role");
        job.ReviseWorkflow(Assessed, DaysAgo(200));
        await fixture.AddJobsAsync(owner.OrganizationId, [job], Ct);
        await fixture.AddApplicationHistoryAsync(
            owner.OrganizationId, job.Id, DaysAgo(20), ApplicationSource.CareersSite,
            [("screening", DaysAgo(19)), ("technical", DaysAgo(18)), ("interview", DaysAgo(8)), ("hired", DaysAgo(7))], cancellationToken: Ct);

        // The hire spent ten days in the technical exercise, which then leaves the pipeline.
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            var saved = await db.Jobs.SingleAsync(j => j.Id == job.Id, Ct);
            saved.ReviseWorkflow(Assessed with { Stages = [.. Assessed.Stages.Where(s => s.Id != "technical")] }, Now);
            await db.SaveChangesAsync(Ct);
        }

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        Stages(report).Should().Equal([("Applied", 1.0, 1), ("Screening", 1.0, 1), ("Interview", 1.0, 1)], "those ten days never join the stage before them");
    }

    [Fact]
    public async Task Open_jobs_list_applications_in_the_range_interviews_now_days_open_and_hires()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var open = TestData.NewJob(owner.OrganizationId, $"open-{Guid.NewGuid():N}", "Open Role");
        open.Publish(DaysAgo(40.5));
        var closed = TestData.NewJob(owner.OrganizationId, $"closed-{Guid.NewGuid():N}", "Closed Role");
        closed.Publish(DaysAgo(60));
        closed.Close(DaysAgo(10));
        var draft = TestData.NewJob(owner.OrganizationId, $"draft-{Guid.NewGuid():N}", "Draft Role");
        await fixture.AddJobsAsync(owner.OrganizationId, [open, closed, draft], Ct);
        await fixture.AddApplicationHistoryAsync(owner.OrganizationId, open.Id, DaysAgo(5), ApplicationSource.CareersSite, [("screening", DaysAgo(4)), ("interview", DaysAgo(3))], cancellationToken: Ct);
        await fixture.AddApplicationHistoryAsync(owner.OrganizationId, open.Id, DaysAgo(4), ApplicationSource.CareersSite, null, cancellationToken: Ct);
        await fixture.AddApplicationHistoryAsync(owner.OrganizationId, open.Id, DaysAgo(10), ApplicationSource.CareersSite, [("hired", DaysAgo(2))], cancellationToken: Ct);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        var row = report.At("openJobs").EnumerateArray().Should().ContainSingle("a closed or draft job is not open").Subject;
        row.GetProperty("jobId").GetGuid().Should().Be(open.Id);
        row.GetProperty("title").GetString().Should().Be("Open Role");
        row.GetProperty("applications").GetInt32().Should().Be(3);
        row.GetProperty("inInterview").GetInt32().Should().Be(1);
        row.GetProperty("openedAt").GetDateTimeOffset().Should().BeCloseTo(open.PublishedAt!.Value, TimeSpan.FromMilliseconds(1), "the column shows the day the job was published");
        row.GetProperty("daysOpen").GetInt32().Should().Be(40);
        row.GetProperty("hires").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_funnel_link_lists_the_applications_that_reached_a_stage_and_only_those_the_person_can_see()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (platform, _) = await SeedAsync(owner.OrganizationId);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(platform, await owner.MemberIdAsync(viewer.UserId));
        using var other = await fixture.SignedInWithOrganizationAsync();
        var (otherPlatform, _) = await SeedAsync(other.OrganizationId);

        var interview = (await owner.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}")).Expect(HttpStatusCode.OK);
        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);
        interview.Count().Should().Be(report.At("headline.reachedInterview").GetInt32(), "the list is the number the report shows");

        // Every one of them is in the candidates list, whatever became of them since (rejected, withdrawn, hired).
        var listed = (await owner.GetAsync("/v1/applications")).Expect(HttpStatusCode.OK).Body.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToHashSet();
        interview.Body.EnumerateArray().Select(id => id.GetGuid()).Should().BeSubsetOf(listed);

        (await viewer.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}")).Expect(HttpStatusCode.OK).Count().Should().Be(3);
        (await owner.GetAsync($"/v1/reports/hiring/funnel/Verification/applications?{Range}")).Status.Should().Be(HttpStatusCode.NotFound, "a verification step has no row (ADR-0004)");
        (await owner.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}&jobId={Guid.NewGuid()}")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        (await owner.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}&jobId={otherPlatform}")).Expect(HttpStatusCode.OK).Count().Should().Be(0, "another organization's job adds nothing");
        var theirs = (await other.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}")).Expect(HttpStatusCode.OK).Body.EnumerateArray().Select(id => id.GetGuid());
        interview.Body.EnumerateArray().Select(id => id.GetGuid()).Should().NotIntersectWith(theirs);
        (await owner.GetAsync("/v1/reports/hiring/funnel/Interview/applications")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().BeEquivalentTo("from", "to");
    }

    [Fact]
    public async Task A_job_filter_narrows_every_panel_and_a_job_nobody_can_see_adds_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (platform, _) = await SeedAsync(owner.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}&jobId={platform}")).Expect(HttpStatusCode.OK);

        Funnel(report).Should().Equal(("Applied", 4, 3), ("Screening", 3, 3), ("Interview", 3, 2), ("Offer", 2, 1), ("Hired", 1, null));
        Jobs(report).Should().Equal((platform, 3, 30.0));
        Sources(report).Should().Equal(("careers", 2, 0), ("recruiter", 1, 0), ("board:talent", 1, 1));

        var unknown = (await owner.GetAsync($"/v1/reports/hiring?{Range}&jobId={Guid.NewGuid()}")).Expect(HttpStatusCode.OK);
        unknown.Count("funnel").Should().Be(0);
        unknown.At("headline.hires").GetInt32().Should().Be(0);
        unknown.At("headline.medianDaysToHire").ValueKind.Should().Be(JsonValueKind.Null);
        unknown.Count("sources").Should().Be(0);
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_sees_only_those_jobs_numbers()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (platform, data) = await SeedAsync(owner.OrganizationId);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(platform, await owner.MemberIdAsync(viewer.UserId));

        var report = (await viewer.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        Funnel(report).Should().Equal(("Applied", 4, 3), ("Screening", 3, 3), ("Interview", 3, 2), ("Offer", 2, 1), ("Hired", 1, null));
        Jobs(report).Should().Equal((platform, 3, 30.0));
        Sources(report).Should().Equal(("careers", 2, 0), ("recruiter", 1, 0), ("board:talent", 1, 1));

        var hidden = (await viewer.GetAsync($"/v1/reports/hiring?{Range}&jobId={data}")).Expect(HttpStatusCode.OK);
        hidden.Count("funnel").Should().Be(0, "filtering by a job they cannot see shows nothing and says nothing about it");
        hidden.At("headline.hires").GetInt32().Should().Be(0);
        hidden.Count("sources").Should().Be(0);
    }

    [Fact]
    public async Task Another_organizations_history_never_counts()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(other.OrganizationId);

        var empty = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        empty.Count("funnel").Should().Be(0);
        empty.At("headline.hires").GetInt32().Should().Be(0);
        empty.Count("openJobs").Should().Be(0);
        empty.Count("sources").Should().Be(0);

        // With a history of its own, the organization's numbers are exactly its own: the other's equal history adds
        // nothing to any panel.
        var (platform, data) = await SeedAsync(owner.OrganizationId);
        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        Funnel(report).Should().Equal(
            ("Applied", 7, 6), ("Screening", 6, 6), ("Assessment", 6, 5), ("Interview", 5, 4), ("Offer", 4, 3), ("Hired", 3, null));
        report.At("headline.hires").GetInt32().Should().Be(5);
        Jobs(report).Should().BeEquivalentTo([(platform, 3, 30.0), (data, 2, 9.5)]);
        Sources(report).Should().Equal(("careers", 3, 0), ("recruiter", 2, 1), ("board:talent", 1, 1), ("website", 1, 1));
    }

    [Fact]
    public async Task A_report_needs_a_range_that_ends_after_it_starts_and_covers_five_years_at_most()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        static string At(DateTimeOffset at) => Uri.EscapeDataString(at.ToString("O"));

        (await owner.GetAsync("/v1/reports/hiring")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().BeEquivalentTo("from", "to");
        (await owner.GetAsync($"/v1/reports/hiring?from={At(Now)}&to={At(Now)}")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("to");
        (await owner.GetAsync($"/v1/reports/hiring?from={At(Now.AddYears(-6))}&to={At(Now)}")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("to");
        (await owner.GetAsync($"/v1/reports/hiring?from={At(Now.AddDays(-30))}&to={At(Now)}&previousFrom={At(Now.AddDays(-60))}"))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("previousTo");
        (await owner.GetAsync($"/v1/reports/hiring?from={At(Now.AddDays(-30))}&to={At(Now)}&timeZone=Not/AZone"))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("timeZone");
    }

    [Fact]
    public async Task Reports_need_a_signed_in_person_or_an_organization_key()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var visitor = new ApiSession(fixture);
        using var stranger = new KeyClient(fixture, "not-a-key");

        using var anonymous = await visitor.Client.GetAsync($"/v1/reports/hiring?{Range}", Ct);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stranger.GetAsync($"/v1/reports/hiring?{Range}")).Status.Should().Be(HttpStatusCode.Unauthorized, "a token that is not a key is no one");
    }

    [Fact]
    public async Task A_read_key_gets_the_same_report_and_funnel_list_as_a_member_who_sees_every_job()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (platform, data) = await SeedAsync(owner.OrganizationId);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(platform, await owner.MemberIdAsync(viewer.UserId));
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Reporting", ["Read"])).Text("token"));

        var viaKey = (await key.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);
        var viaSession = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        Funnel(viaKey).Should().Equal(Funnel(viaSession)).And.Equal(
            ("Applied", 7, 6), ("Screening", 6, 6), ("Assessment", 6, 5), ("Interview", 5, 4), ("Offer", 4, 3), ("Hired", 3, null));
        Jobs(viaKey).Should().BeEquivalentTo([(platform, 3, 30.0), (data, 2, 9.5)], "a key sees every job, as the MCP tool does (ADR-0015)");
        Sources(viaKey).Should().Equal(Sources(viaSession));
        viaKey.At("headline.hires").GetInt32().Should().Be(5);
        viaKey.Body.GetRawText().Should().NotContainEquivalentOf("verification", "a report opens nothing of verification to a Read key (ADR-0021)");
        Jobs((await viewer.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK)).Select(j => j.Item1).Should().Equal(platform)
            .And.NotBeEquivalentTo(Jobs(viaKey).Select(j => j.Item1), "a member on Only jobs they're on sees less than a key");

        var narrowed = (await key.GetAsync($"/v1/reports/hiring?{Range}&jobId={data}&timeZone=Europe/London")).Expect(HttpStatusCode.OK);
        Jobs(narrowed).Should().Equal((data, 2, 9.5));

        var interview = (await key.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}")).Expect(HttpStatusCode.OK);
        interview.Body.EnumerateArray().Select(id => id.GetGuid()).Should().BeEquivalentTo(
            (await owner.GetAsync($"/v1/reports/hiring/funnel/Interview/applications?{Range}")).Expect(HttpStatusCode.OK)
                .Body.EnumerateArray().Select(id => id.GetGuid()));
        (await key.GetAsync($"/v1/reports/hiring?from={Uri.EscapeDataString(Now.ToString("O"))}&to={Uri.EscapeDataString(Now.ToString("O"))}"))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("to");
    }

    [Fact]
    public async Task A_key_reads_only_its_own_organizations_numbers()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(other.OrganizationId);
        var (platform, _) = await SeedAsync(owner.OrganizationId);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Reporting", ["Read"])).Text("token"));
        using var theirKey = new KeyClient(fixture, (await other.CreateKeyAsync("Reporting", ["Read"])).Text("token"));

        var ours = (await key.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);
        var theirs = (await theirKey.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        ours.At("headline.hires").GetInt32().Should().Be(5, "the other organization's equal history adds nothing");
        Jobs(ours).Select(j => j.Item1).Should().Contain(platform);
        Jobs(theirs).Select(j => j.Item1).Should().NotContain(platform);
        (await key.GetAsync($"/v1/reports/hiring?{Range}&jobId={Jobs(theirs).First().Item1}")).Expect(HttpStatusCode.OK)
            .Count("funnel").Should().Be(0, "another organization's job adds nothing");
    }

    /// <summary>
    /// Platform (the default pipeline) and Data (with an assessment and a verification step), and their applications.
    /// Seven arrive in the range; three more arrive before it, two of them hired inside it.
    /// </summary>
    private async Task<(Guid Platform, Guid Data)> SeedAsync(Guid organizationId)
    {
        var platform = TestData.NewJob(organizationId, $"platform-{Guid.NewGuid():N}", "Platform Engineer");
        var data = TestData.NewJob(organizationId, $"data-{Guid.NewGuid():N}", "Data Engineer");
        data.ReviseWorkflow(Assessed, DaysAgo(200));
        await fixture.AddJobsAsync(organizationId, [platform, data], Ct);

        Task<Guid> Add(Job job, double appliedDaysAgo, ApplicationSource source, params (string Stage, double DaysAgo)[] moves) =>
            fixture.AddApplicationHistoryAsync(
                organizationId, job.Id, DaysAgo(appliedDaysAgo), source, moves.Select(m => (m.Stage, DaysAgo(m.DaysAgo))), cancellationToken: Ct);

        // Through a job board's apply link, and through the careers page embedded in the organization's own site.
        Task<Guid> AddFrom(Job job, double appliedDaysAgo, string? board, string? site, params (string Stage, double DaysAgo)[] moves) =>
            fixture.AddApplicationHistoryAsync(
                organizationId, job.Id, DaysAgo(appliedDaysAgo), ApplicationSource.CareersSite, moves.Select(m => (m.Stage, DaysAgo(m.DaysAgo))), cancellationToken: Ct,
                sourceSite: site, sourceBoard: board);

        // Platform, in the range.
        await AddFrom(platform, 10, "talent", null, ("screening", 9), ("interview", 8), ("offer", 7), ("hired", 5));
        await Add(platform, 12, ApplicationSource.Recruiter, ("interview", 11), ("rejected", 10));
        await Add(platform, 6, ApplicationSource.CareersSite, ("screening", 5), ("interview", 4), ("offer", 3), ("hired", 2), ("offer", 1));
        await Add(platform, 20, ApplicationSource.CareersSite);

        // Platform, applied before the range: two hired inside it, one hired before it.
        await Add(platform, 60, ApplicationSource.CareersSite, ("hired", 15));
        await Add(platform, 40, ApplicationSource.Recruiter, ("interview", 30.5), ("hired", 10));
        await Add(platform, 100, ApplicationSource.CareersSite, ("hired", 90));

        // Data, in the range: rejected after the verification step, hired through it, and hired the usual way.
        await Add(data, 15, ApplicationSource.CareersSite, ("screening", 14), ("technical", 13), ("id-check", 12), ("rejected", 11));
        await Add(data, 14, ApplicationSource.Recruiter, ("id-check", 13), ("interview", 12), ("offer", 11), ("hired", 9));
        await AddFrom(data, 25, null, "https://www.example.com", ("screening", 24), ("technical", 23), ("interview", 22), ("offer", 21), ("hired", 11));

        return (platform.Id, data.Id);
    }

    private static IEnumerable<(string, int, int?)> Funnel(ApiResult report) =>
        report.At("funnel").EnumerateArray().Select(s => (
            s.GetProperty("kind").GetString()!,
            s.GetProperty("applications").GetInt32(),
            s.GetProperty("movedOn").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("movedOn").GetInt32()));

    private static IEnumerable<(Guid, int, double)> Jobs(ApiResult report) =>
        report.At("timeToHire.jobs").EnumerateArray().Select(j => (
            j.GetProperty("jobId").GetGuid(), j.GetProperty("hires").GetInt32(), Math.Round(j.GetProperty("medianDays").GetDouble(), 2)));

    private static IEnumerable<(string, double, int)> Stages(ApiResult report) =>
        report.At("timeToHire.stages").EnumerateArray().Select(s => (
            s.GetProperty("kind").GetString()!, Math.Round(s.GetProperty("medianDays").GetDouble(), 2), s.GetProperty("hires").GetInt32()));

    private static IEnumerable<(string, int, int)> Sources(ApiResult report) =>
        report.At("sources").EnumerateArray().Select(s => (
            s.GetProperty("key").GetString()!, s.GetProperty("applications").GetInt32(), s.GetProperty("hires").GetInt32()));
}
