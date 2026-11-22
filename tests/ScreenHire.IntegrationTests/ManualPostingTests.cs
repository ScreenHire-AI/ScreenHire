using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Postings the team makes themselves on boards ScreenHire does not post to (docs/architecture/job-distribution.md, "Posted by
/// you"): a saved posting's tracked link names its board on the applications that came through it, in Source and in Reports;
/// every link of a job has its count; the row says when the job changed since it was posted; and none of it crosses
/// organizations.
/// </summary>
public sealed class ManualPostingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Range =
        $"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}";

    private static JsonObject Posting(string boardName, string? sourceKey = null, string? listingUrl = null, string? postedOn = null, string? endsOn = null) => new()
    {
        ["boardName"] = boardName,
        ["sourceKey"] = sourceKey,
        ["listingUrl"] = listingUrl,
        ["postedOn"] = postedOn,
        ["endsOn"] = endsOn,
    };

    private static Dictionary<string, int> Counts(ApiResult result) =>
        result.Body.EnumerateArray().ToDictionary(c => c.GetProperty("source").GetString()!, c => c.GetProperty("applications").GetInt32());

    private static List<string> Changed(ApiResult posting) =>
        [.. posting.At("changedFields").EnumerateArray().Select(f => f.GetString()!)];

    [Fact]
    public async Task Applications_through_a_saved_postings_link_name_its_board_and_every_link_is_counted()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);

        var saved = (await owner.PostAsync(
                $"/v1/jobs/{jobId}/manual-postings",
                Posting("Riverside Jobs Weekly", sourceKey: "riverside-jobs-weekly", listingUrl: "https://jobs.example.org/listing/42", postedOn: "2026-11-20")))
            .Expect(HttpStatusCode.Created);
        saved.Text("sourceKey").Should().Be("riverside-jobs-weekly");
        saved.Text("trackedUrl").Should().Contain("/jobs/").And.EndWith("?src=riverside-jobs-weekly");
        saved.Text("listingUrl").Should().Be("https://jobs.example.org/listing/42");
        saved.Count("changedFields").Should().Be(0, "the posting holds the job as it is now");

        // Renaming the board keeps its link, so one already pasted on the board keeps counting.
        (await owner.PutAsync($"/v1/jobs/{jobId}/manual-postings/{saved.Id()}", new JsonObject
            {
                ["boardName"] = "Riverside Weekly",
                ["listingUrl"] = "https://jobs.example.org/listing/42",
                ["postedOn"] = "2026-11-20",
            }))
            .Expect(HttpStatusCode.OK).Text("sourceKey").Should().Be("riverside-jobs-weekly");

        var viaPosting = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.GenuineHuman, Ct, source: "riverside-jobs-weekly");
        await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.HonestAiAssisted, Ct, source: "riverside-jobs-weekly");
        await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.ApplicationFarm[0], Ct, source: "talent");
        await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.ApplicationFarm[1], Ct);
        var unknown = await fixture.ApplyAsync(slug, jobId, SyntheticApplicants.Bot, Ct, source: "no-such-board");

        Counts((await owner.GetAsync($"/v1/jobs/{jobId}/source-applications")).Expect(HttpStatusCode.OK)).Should().BeEquivalentTo(
            new Dictionary<string, int> { ["riverside-jobs-weekly"] = 2, ["talent"] = 1, ["careers"] = 2 },
            "a key no posting of the job has is dropped, so that application counts as the careers page");

        var detail = (await owner.GetAsync($"/v1/applications/{viaPosting}")).Expect(HttpStatusCode.OK);
        detail.Text("sourceBoard").Should().Be("riverside-jobs-weekly");
        detail.Text("sourceBoardName").Should().Be("Riverside Weekly", "Source names the board as the team named it last");
        (await owner.GetAsync($"/v1/applications/{unknown}")).Expect(HttpStatusCode.OK).At("sourceBoard").ValueKind.Should().Be(JsonValueKind.Null);
        var list = (await owner.GetAsync($"/v1/jobs/{jobId}/applications")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        list.Single(a => a.GetProperty("id").GetGuid() == viaPosting).GetProperty("sourceBoardName").GetString().Should().Be("Riverside Weekly");
        list.Single(a => a.GetProperty("sourceBoard").GetString() == "talent").GetProperty("sourceBoardName").GetString().Should().Be("Talent.com");

        var sources = (await owner.GetAsync($"/v1/reports/hiring?{Range}")).Expect(HttpStatusCode.OK).At("sources").EnumerateArray()
            .ToDictionary(s => s.GetProperty("key").GetString()!, s => (Name: s.GetProperty("name").GetString(), Applications: s.GetProperty("applications").GetInt32()));
        sources["board:riverside-jobs-weekly"].Should().Be(("Riverside Weekly", 2));
    }

    [Fact]
    public async Task The_row_says_what_changed_in_the_job_since_it_was_posted_until_it_is_marked_updated()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var posting = (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("LinkedIn"))).Expect(HttpStatusCode.Created);
        posting.Text("sourceKey").Should().Be("linkedin", "a key left out is made from the board's name");

        var job = (await owner.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);
        var renamed = Samples.RemoteJob("Platform Engineer (Lead)");
        renamed["slug"] = job.Text("slug");
        (await owner.PutAsync($"/v1/jobs/{jobId}", renamed)).Expect(HttpStatusCode.OK);

        var stale = (await owner.GetAsync($"/v1/jobs/{jobId}/manual-postings")).Expect(HttpStatusCode.OK).At("postings").EnumerateArray().Single();
        stale.GetProperty("changedFields").EnumerateArray().Select(f => f.GetString()).Should().Equal(["Title"],
            "only the title changed: the job's other copied fields are what was posted");

        var updated = (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings/{posting.Id()}/updated")).Expect(HttpStatusCode.OK);
        Changed(updated).Should().BeEmpty();
        (await owner.GetAsync($"/v1/jobs/{jobId}/manual-postings")).Expect(HttpStatusCode.OK).At("postings").EnumerateArray().Single()
            .GetProperty("changedFields").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_closed_jobs_posting_stays_listed_until_it_is_marked_taken_down_and_the_board_can_be_posted_again()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);
        var posting = (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Indeed"))).Expect(HttpStatusCode.Created);

        // The same board twice while the first is up is a 409, never a duplicate link.
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("indeed"))).Expect(HttpStatusCode.Conflict);
        // Taken down, the board can be posted again: one row per link, so the old link and the new one count together.
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings/{posting.Id()}/taken-down")).Expect(HttpStatusCode.OK).At("takenDownAt").ValueKind
            .Should().Be(JsonValueKind.String);
        var again = (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Indeed", listingUrl: "https://www.indeed.example/job/1")))
            .Expect(HttpStatusCode.Created);
        again.Id().Should().Be(posting.Id());
        again.At("takenDownAt").ValueKind.Should().Be(JsonValueKind.Null);

        await owner.CloseJobAsync(jobId);
        var listed = (await owner.GetAsync($"/v1/jobs/{jobId}/manual-postings")).Expect(HttpStatusCode.OK).At("postings").EnumerateArray().Single();
        listed.GetProperty("takenDownAt").ValueKind.Should().Be(JsonValueKind.Null, "closing the job cannot take down a listing ScreenHire did not make");

        // Edit and Mark as taken down still work on the closed job: tidying up after a close is what the row asks for.
        (await owner.PutAsync($"/v1/jobs/{jobId}/manual-postings/{posting.Id()}", new JsonObject
            {
                ["boardName"] = "Indeed",
                ["postedOn"] = "2026-11-20",
                ["endsOn"] = "2026-11-21",
            }))
            .Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings/{posting.Id()}/taken-down")).Expect(HttpStatusCode.OK);
        // A closed job takes no new posting: its careers page takes no applications.
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Glassdoor"))).Expect(HttpStatusCode.Conflict);

        await using var db = await TenantDbAsync(owner.OrganizationId);
        (await db.ManualPostings.AsNoTracking().CountAsync(p => p.JobId == jobId, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task A_posting_is_refused_for_a_board_ScreenHire_posts_to_an_address_that_is_not_a_web_page_and_a_draft()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (_, jobId) = await VerificationSupport.OpenJobAsync(fixture, owner);

        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Adzuna"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Contain("boardName");
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Local", sourceKey: "careers"))).Expect(HttpStatusCode.BadRequest);
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Local", listingUrl: "javascript:alert(1)"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Contain("listingUrl");
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Local", sourceKey: "Not A Key"))).Expect(HttpStatusCode.BadRequest);
        (await owner.PostAsync($"/v1/jobs/{jobId}/manual-postings", Posting("Local", postedOn: "2026-11-20", endsOn: "2026-11-19"))).Expect(HttpStatusCode.BadRequest);

        var draft = (await owner.CreateJobAsync(Samples.RemoteJob("Not yet out"))).Id();
        (await owner.PostAsync($"/v1/jobs/{draft}/manual-postings", Posting("LinkedIn"))).Expect(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task One_organizations_postings_and_counts_never_reach_another()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        var (firstSlug, firstJob) = await VerificationSupport.OpenJobAsync(fixture, first);
        var (secondSlug, secondJob) = await VerificationSupport.OpenJobAsync(fixture, second);
        var posting = (await first.PostAsync($"/v1/jobs/{firstJob}/manual-postings", Posting("LinkedIn"))).Expect(HttpStatusCode.Created);

        (await second.GetAsync($"/v1/jobs/{firstJob}/manual-postings")).Expect(HttpStatusCode.NotFound);
        (await second.GetAsync($"/v1/jobs/{firstJob}/source-applications")).Expect(HttpStatusCode.NotFound);
        (await second.PostAsync($"/v1/jobs/{firstJob}/manual-postings", Posting("Indeed"))).Expect(HttpStatusCode.NotFound);
        (await second.PostAsync($"/v1/jobs/{firstJob}/manual-postings/{posting.Id()}/taken-down")).Expect(HttpStatusCode.NotFound);
        // The posting's id under the second organization's own job finds nothing either.
        (await second.PostAsync($"/v1/jobs/{secondJob}/manual-postings/{posting.Id()}/taken-down")).Expect(HttpStatusCode.NotFound);

        // The first organization's key means nothing on the second's job: no posting of that job has it.
        var elsewhere = await fixture.ApplyAsync(secondSlug, secondJob, SyntheticApplicants.GenuineHuman, Ct, source: "linkedin");
        (await second.GetAsync($"/v1/applications/{elsewhere}")).Expect(HttpStatusCode.OK).At("sourceBoard").ValueKind.Should().Be(JsonValueKind.Null);
        await fixture.ApplyAsync(firstSlug, firstJob, SyntheticApplicants.HonestAiAssisted, Ct, source: "linkedin");
        Counts((await first.GetAsync($"/v1/jobs/{firstJob}/source-applications")).Expect(HttpStatusCode.OK)).Should().BeEquivalentTo(new Dictionary<string, int> { ["linkedin"] = 1 });
        Counts((await second.GetAsync($"/v1/jobs/{secondJob}/source-applications")).Expect(HttpStatusCode.OK)).Should().BeEquivalentTo(new Dictionary<string, int> { ["careers"] = 1 });
    }

    private async Task<ScreenHire.Infrastructure.Persistence.ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
