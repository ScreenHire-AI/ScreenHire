using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Tenancy;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests.Activity;

/// <summary>
/// The workspace's Activity feed, <c>GET /v1/activity</c> (docs/architecture/notifications.md, "Activity"): every
/// application's timeline and every job's changes, newest first and paged, only what the reader may see (their
/// organization, and under job access the jobs whose team they are on), with who made each change, no verification
/// level, no scorecard a reviewer may not read yet, and a count of what came since they last looked.
/// </summary>
public sealed class ActivityFeedTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Another_organizations_changes_and_a_hidden_jobs_never_appear()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var visible = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var hidden = (await owner.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"))).Id();
        var seen = (await owner.CreateApplicationAsync(visible, ApiSession.NewEmail("seen"))).Id();
        var unseen = (await owner.CreateApplicationAsync(hidden, ApiSession.NewEmail("unseen"))).Id();
        var otherJob = (await theirs.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var otherApplication = (await theirs.CreateApplicationAsync(otherJob, ApiSession.NewEmail("other"))).Id();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.SetHiringTeamAsync(visible, await owner.MemberIdAsync(owner.UserId), await owner.MemberIdAsync(viewer.UserId));

        var ownerFeed = await FeedAsync(owner, "?scope=Everything");
        JobsOf(ownerFeed).Should().BeEquivalentTo([visible, hidden]);
        ApplicationsOf(ownerFeed).Should().BeEquivalentTo([seen, unseen]);
        ownerFeed.Should().Contain(e => e.GetProperty("kind").GetString() == "Jobs" && e.At("jobChange.created").GetBoolean()
            && e.GetProperty("jobId").GetGuid() == visible, "the job's creation is in its history");

        var viewerFeed = await FeedAsync(viewer, "?scope=Everything");
        JobsOf(viewerFeed).Should().Equal([visible], "the payroll job's applications and its history are hidden from them");
        ApplicationsOf(viewerFeed).Should().Equal(seen);
        ApplicationsOf(await FeedAsync(viewer, "?scope=MyJobs")).Should().Equal(seen);

        foreach (var feed in new[] { ownerFeed, viewerFeed })
        {
            JobsOf(feed).Should().NotContain(otherJob);
            ApplicationsOf(feed).Should().NotContain(otherApplication);
        }

        JobsOf(await FeedAsync(theirs, "?scope=Everything")).Should().Equal([otherJob]);
    }

    [Fact]
    public async Task My_jobs_and_the_kinds_narrow_it_and_the_persons_own_scope_is_the_default()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var mine = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        // The recruiter makes the job, so they are on its team and the owner is not.
        var theirs = (await recruiter.CreateJobAsync(Samples.RemoteJob("Data Engineer"))).Id();
        var application = (await owner.CreateApplicationAsync(mine, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        await recruiter.CreateApplicationAsync(theirs, ApiSession.NewEmail("candidate"));

        JobsOf(await FeedAsync(owner, "?scope=MyJobs")).Should().Equal([mine]);
        JobsOf(await FeedAsync(owner)).Should().Equal([mine], "My jobs is where Your account starts");
        JobsOf(await FeedAsync(owner, "?scope=Everything")).Should().BeEquivalentTo([mine, theirs]);

        var moves = await FeedAsync(owner, "?scope=Everything&kinds=Moves");
        moves.Should().ContainSingle().Which.At("event.type").GetString().Should().Be("StageChanged");
        moves[0].GetProperty("toStageName").GetString().Should().Be("Screening");
        moves[0].GetProperty("by").GetString().Should().Be("Member");
        moves[0].GetProperty("actorUserId").GetGuid().Should().Be(owner.UserId);
        var two = await FeedAsync(owner, "?scope=Everything&kinds=Moves&kinds=Jobs");
        two.Select(e => e.GetProperty("kind").GetString()).Should().OnlyContain(kind => kind == "Moves" || kind == "Jobs");
        two.Count(e => e.GetProperty("kind").GetString() == "Jobs").Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task It_pages_newest_first_across_timelines_and_job_changes_without_gaps_or_repeats()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        for (var i = 0; i < 3; i++)
        {
            var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
            (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        }

        await owner.PublishJobAsync(job);
        var all = await FeedAsync(owner);
        all.Select(e => e.GetProperty("kind").GetString()).Should().Contain(["Jobs", "Applications", "Moves"]);
        all.Select(e => e.GetProperty("occurredAt").GetDateTimeOffset()).Should().BeInDescendingOrder();

        var paged = new List<Guid>();
        string? before = null;
        do
        {
            var page = (await owner.GetAsync($"/v1/activity?limit=2{(before is null ? "" : $"&before={before}")}")).Expect(HttpStatusCode.OK);
            page.Count("entries").Should().BeLessThanOrEqualTo(2);
            paged.AddRange(page.At("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
            before = page.At("nextBefore").ValueKind == JsonValueKind.Null ? null : page.Text("nextBefore");
        }
        while (before is not null);

        paged.Should().Equal(all.Select(e => e.GetProperty("id").GetGuid()));
        (await owner.GetAsync("/v1/activity?before=not-a-cursor")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("before");
        (await owner.GetAsync("/v1/activity?limit=0")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("limit");
    }

    [Fact]
    public async Task Keys_and_automations_are_named_and_checks_say_only_that_they_finished()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("ATS sync", ["Read", "Write"])).Text("token"));
        (await key.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        (await key.GetAsync("/v1/activity")).Status.Should().Be(HttpStatusCode.Unauthorized, "a key is no person and has no feed");

        // A verification run and an automation's tag, as the worker writes them.
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        var assessed = Guid.CreateVersion7();
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            db.ApplicationEvents.Add(new ApplicationEvent
            {
                Id = assessed,
                OrganizationId = owner.OrganizationId,
                ApplicationId = application,
                Type = ApplicationEventType.VerificationAssessed,
                Data = new ApplicationEventData(
                    AuthenticityLevel: nameof(AuthenticityLevel.VerificationRecommended), AiAssistanceLevel: "Substantial", RulesetVersion: "test"),
                OccurredAt = DateTimeOffset.UtcNow,
            });
            db.ApplicationEvents.Add(new ApplicationEvent
            {
                OrganizationId = owner.OrganizationId,
                ApplicationId = application,
                Type = ApplicationEventType.TagAdded,
                Data = new ApplicationEventData(Tag: "referral", AutomationRunId: Guid.CreateVersion7()),
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        var feed = await FeedAsync(owner);
        var moved = feed.Single(e => TypeOf(e) == "StageChanged");
        moved.GetProperty("by").GetString().Should().Be("ApiKey");
        moved.GetProperty("actorApiKeyName").GetString().Should().Be("ATS sync");
        var tagged = feed.Single(e => TypeOf(e) == "TagAdded");
        tagged.GetProperty("by").GetString().Should().Be("Automation");
        // The worker's own run is in the feed too; neither says what the checks found.
        var checks = feed.Where(e => TypeOf(e) == "VerificationAssessed").ToList();
        checks.Select(e => e.GetProperty("id").GetGuid()).Should().Contain(assessed);
        foreach (var entry in checks)
        {
            entry.GetProperty("kind").GetString().Should().Be("Applications");
            var text = entry.GetRawText();
            text.Should().NotContainEquivalentOf("level", "the feed says the checks finished, never what they found (non-negotiable 1)");
            text.Should().NotContain("VerificationRecommended").And.NotContain("Substantial");
        }
    }

    [Fact]
    public async Task A_reviewer_with_their_own_scorecard_to_give_cannot_open_anothers_and_nobody_reads_a_recommendation()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        var scorecard = (await owner.PostAsync($"/v1/applications/{application}/scorecards", Submission("interview", "Yes", InterviewRatings(4, 4, 4))))
            .Expect(HttpStatusCode.Created).Id();
        (await owner.PostAsync($"/v1/applications/{application}/scorecard-requests", new JsonObject
        {
            ["assigneeUserId"] = teammate.UserId.ToString(), ["scorecardTemplateId"] = "interview",
        })).Expect(HttpStatusCode.Created);

        static JsonElement Submitted(IReadOnlyList<JsonElement> feed) =>
            feed.Single(e => TypeOf(e) == "ScorecardSubmitted");

        var theirs = Submitted(await FeedAsync(teammate, "?scope=Everything"));
        theirs.At("event.scorecardId").ValueKind.Should().Be(JsonValueKind.Null, "they see that it was given, not a way to read it (ADR-0013)");
        theirs.At("event.recommendation").ValueKind.Should().Be(JsonValueKind.Null);
        var own = Submitted(await FeedAsync(owner, "?scope=Everything"));
        own.At("event.scorecardId").GetGuid().Should().Be(scorecard);
        own.At("event.recommendation").ValueKind.Should().Be(JsonValueKind.Null, "the feed says a scorecard came in, not what it said");
    }

    [Fact]
    public async Task Seen_counts_teammates_changes_since_and_never_the_persons_own()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var first = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("first"))).Id();
        var second = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("second"))).Id();

        var before = (await owner.GetAsync("/v1/activity")).Expect(HttpStatusCode.OK);
        before.At("seenAt").ValueKind.Should().Be(JsonValueKind.Null, "they have never opened it");
        before.At("unseenCount").ValueKind.Should().Be(JsonValueKind.Null);

        (await owner.PostAsync("/v1/activity/seen")).Expect(HttpStatusCode.NoContent);
        var seen = (await owner.GetAsync("/v1/activity")).Expect(HttpStatusCode.OK);
        var seenAt = seen.At("seenAt").GetDateTimeOffset();
        seen.At("unseenCount").GetInt32().Should().Be(0);

        (await recruiter.PostAsync($"/v1/applications/{first}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{second}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var after = (await owner.GetAsync("/v1/activity")).Expect(HttpStatusCode.OK);
        after.At("unseenCount").GetInt32().Should().Be(1, "the recruiter's move is news; the owner's own is not");
        after.At("seenAt").GetDateTimeOffset().Should().Be(seenAt);

        (await owner.PostAsync("/v1/activity/seen")).Expect(HttpStatusCode.NoContent);
        var again = (await owner.GetAsync("/v1/activity")).Expect(HttpStatusCode.OK);
        again.At("unseenCount").GetInt32().Should().Be(0);
        again.At("seenAt").GetDateTimeOffset().Should().BeOnOrAfter(seenAt);
        (await recruiter.GetAsync("/v1/activity")).Expect(HttpStatusCode.OK).At("seenAt").ValueKind
            .Should().Be(JsonValueKind.Null, "each person keeps their own time");
    }

    private static async Task<IReadOnlyList<JsonElement>> FeedAsync(Tenant reader, string query = "")
    {
        var separator = query.Length == 0 ? "?" : "&";
        var page = (await reader.GetAsync($"/v1/activity{query}{separator}limit=100")).Expect(HttpStatusCode.OK);
        return [.. page.At("entries").EnumerateArray()];
    }

    // The timeline entry's type, or null for a job's change, which has no entry.
    private static string? TypeOf(JsonElement entry) =>
        entry.GetProperty("event") is { ValueKind: JsonValueKind.Object } timeline ? timeline.GetProperty("type").GetString() : null;

    private static List<Guid> JobsOf(IEnumerable<JsonElement> feed) => [.. feed.Select(e => e.GetProperty("jobId").GetGuid()).Distinct()];

    private static List<Guid> ApplicationsOf(IEnumerable<JsonElement> feed) =>
        [.. feed.Where(e => e.GetProperty("applicationId").ValueKind == JsonValueKind.String).Select(e => e.GetProperty("applicationId").GetGuid()).Distinct()];
}
