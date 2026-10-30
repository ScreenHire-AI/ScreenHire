using System.Text.Json;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;

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
        report.At("timeToHire.hires").GetInt32().Should().Be(5);
        report.At("timeToHire.medianDays").GetDouble().Should().BeApproximately(14, 0.01, "the median of every hire, not of the jobs' medians");
        Jobs(report).Should().BeEquivalentTo([(platform, 3, 30.0), (data, 2, 9.5)]);

        Sources(report).Should().Equal(("CareersSite", 5, 2), ("Recruiter", 2, 1));
    }

    [Fact]
    public async Task A_job_filter_narrows_every_panel_and_a_job_nobody_can_see_adds_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (platform, _) = await SeedAsync(owner.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}&jobId={platform}")).Expect(HttpStatusCode.OK);

        Funnel(report).Should().Equal(("Applied", 4, 3), ("Screening", 3, 3), ("Interview", 3, 2), ("Offer", 2, 1), ("Hired", 1, null));
        Jobs(report).Should().Equal((platform, 3, 30.0));
        Sources(report).Should().Equal(("CareersSite", 3, 1), ("Recruiter", 1, 0));

        var unknown = (await owner.GetAsync($"/v1/reports/hiring?{Range}&jobId={Guid.NewGuid()}")).Expect(HttpStatusCode.OK);
        unknown.Count("funnel").Should().Be(0);
        unknown.At("timeToHire.hires").GetInt32().Should().Be(0);
        unknown.At("timeToHire.medianDays").ValueKind.Should().Be(JsonValueKind.Null);
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
        Sources(report).Should().Equal(("CareersSite", 3, 1), ("Recruiter", 1, 0));

        var hidden = (await viewer.GetAsync($"/v1/reports/hiring?{Range}&jobId={data}")).Expect(HttpStatusCode.OK);
        hidden.Count("funnel").Should().Be(0, "filtering by a job they cannot see shows nothing and says nothing about it");
        hidden.At("timeToHire.hires").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Another_organizations_history_never_counts()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        await SeedAsync(other.OrganizationId);

        var report = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK);

        report.Count("funnel").Should().Be(0);
        report.At("timeToHire.hires").GetInt32().Should().Be(0);
        report.Count("sources").Should().Be(0);
    }

    [Fact]
    public async Task A_report_needs_a_range_that_ends_after_it_starts_and_covers_five_years_at_most()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        static string At(DateTimeOffset at) => Uri.EscapeDataString(at.ToString("O"));

        (await owner.GetAsync("/v1/reports/hiring")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().BeEquivalentTo("from", "to");
        (await owner.GetAsync($"/v1/reports/hiring?from={At(Now)}&to={At(Now)}")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("to");
        (await owner.GetAsync($"/v1/reports/hiring?from={At(Now.AddYears(-6))}&to={At(Now)}")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("to");
    }

    [Fact]
    public async Task Reports_are_for_signed_in_people_only()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var created = await owner.CreateKeyAsync("Everything", ["Read", "Write"]);
        using var key = new KeyClient(fixture, created.Text("token"));
        using var visitor = new ApiSession(fixture);

        (await key.GetAsync($"/v1/reports/hiring?{Range}")).Status.Should().Be(HttpStatusCode.Unauthorized, "reports are not part of the hiring API");
        using var anonymous = await visitor.Client.GetAsync($"/v1/reports/hiring?{Range}", Ct);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
                organizationId, job.Id, DaysAgo(appliedDaysAgo), source, moves.Select(m => (m.Stage, DaysAgo(m.DaysAgo))), Ct);

        // Platform, in the range.
        await Add(platform, 10, ApplicationSource.CareersSite, ("screening", 9), ("interview", 8), ("offer", 7), ("hired", 5));
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
        await Add(data, 25, ApplicationSource.CareersSite, ("screening", 24), ("technical", 23), ("interview", 22), ("offer", 21), ("hired", 11));

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

    private static IEnumerable<(string, int, int)> Sources(ApiResult report) =>
        report.At("sources").EnumerateArray().Select(s => (
            s.GetProperty("source").GetString()!, s.GetProperty("applications").GetInt32(), s.GetProperty("hires").GetInt32()));
}
