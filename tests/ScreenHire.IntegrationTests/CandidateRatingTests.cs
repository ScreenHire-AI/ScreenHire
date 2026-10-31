using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Integrity;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The candidate's rating (ADR-0013 decision 13, workflow-engine.md section 8): one to five from people's rating answers
/// and nothing else, on the application, the job's list, the all-applications list and the candidate. It is hidden from a
/// member who has an open Review task of their own to answer, and from the other scorecards, the recommendations on the
/// timeline and the reviewer decision's facts, until they submit. No automation condition may read it, and an integrity
/// result never changes it.
/// </summary>
public sealed class CandidateRatingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_rating_averages_each_scorecard_once_maps_the_scales_and_rounds_only_at_the_end()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var first = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var second = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var scorecard = await CreateScorecardAsync(owner, "Panel review",
            Rating5("r5"), Question("r10", "Rating10", "Problem solving"), Question("ryn", "RatingYesNo", "Meets the requirements"), Question("st", "ShortText", "In a few words"));
        var job = await JobNamingAsync(owner, "Platform Engineer", "interview", scorecard);
        var unrated = await InInterviewAsync(owner, job);
        var shared = await InInterviewAsync(owner, job);
        var tenOnly = await InInterviewAsync(owner, job);
        var tenTop = await InInterviewAsync(owner, job);
        var no = await InInterviewAsync(owner, job);
        var textOnly = await InInterviewAsync(owner, job);

        // One candidate, three scorecards: 4.67 (a 4, a 10 that maps to 5 and a Yes that maps to 5), a 2, and one with no
        // rating answer at all, which counts for nothing. The rating is the mean of the first two, 3.33, not of the rounded 4.7
        // and 2.0 (which would show 3.4).
        var ownerCard = (await owner.PostAsync(Scorecards(shared), Submission("interview", "Yes", Answer("r5", rating: 4), Answer("r10", rating: 10), Answer("ryn", "yes"))))
            .Expect(HttpStatusCode.Created);
        var firstCard = (await first.PostAsync(Scorecards(shared), Submission("interview", "No", Answer("r5", rating: 2)))).Expect(HttpStatusCode.Created);
        var secondCard = (await second.PostAsync(Scorecards(shared), Submission("interview", "Yes", Answer("st", "Friendly and direct.")))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync(Scorecards(tenOnly), Submission("interview", "Yes", Answer("r10", rating: 5)))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync(Scorecards(tenTop), Submission("interview", "Yes", Answer("r10", rating: 10)))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync(Scorecards(no), Submission("interview", "No", Answer("ryn", "no")))).Expect(HttpStatusCode.Created);
        var textCard = (await owner.PostAsync(Scorecards(textOnly), Submission("interview", "Yes", Answer("st", "Quiet.")))).Expect(HttpStatusCode.Created);

        ownerCard.At("rating").GetDecimal().Should().Be(4.7m);
        firstCard.At("rating").GetDecimal().Should().Be(2m);
        secondCard.At("rating").ValueKind.Should().Be(JsonValueKind.Null, "a scorecard without a rating answer has no rating");
        textCard.At("rating").ValueKind.Should().Be(JsonValueKind.Null);
        var list = (await owner.GetAsync($"/v1/jobs/{job}/applications")).Expect(HttpStatusCode.OK);
        Described(list, unrated).Should().Be("NotRated - x0");
        Described(list, shared).Should().Be("Rated 3.3 x2");
        Described(list, tenOnly).Should().Be("Rated 2.8 x1", "a 5 out of 10 is 1 + 4 * 4 / 9 on the 1 to 5 scale");
        Described(list, tenTop).Should().Be("Rated 5.0 x1");
        Described(list, no).Should().Be("Rated 1.0 x1", "a rating No counts as 1");
        Described(list, textOnly).Should().Be("NotRated - x0", "a scorecard with no rating answer counts for nothing");

        // The same value on every surface that shows the rating, for every reader.
        var candidate = (await owner.GetAsync($"/v1/applications/{shared}")).Id("candidate.id");
        foreach (var reader in new[] { owner, first, second })
        {
            (await RatingsAsync(reader.GetAsync, job, shared, candidate)).Values.Should().OnlyContain(r => r == "Rated 3.3 x2");
            (await RatingsAsync(reader.GetAsync, job, unrated, (await reader.GetAsync($"/v1/applications/{unrated}")).Id("candidate.id")))
                .Values.Should().OnlyContain(r => r == "NotRated - x0");
        }
    }

    [Fact]
    public async Task A_reviewer_with_a_review_to_give_reads_the_rating_only_after_their_own_scorecard_and_everyone_else_reads_it_throughout()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var bystander = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var (job, application) = await ApplicationWithReviewAsync(owner, assignee: recruiter.UserId);
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Id("candidate.id");
        var tasks = (await owner.GetAsync($"/v1/applications/{application}/tasks")).Expect(HttpStatusCode.OK);
        var url = Scorecards(application);

        // Another member gives a scorecard: 4, 5 and 5 on the interview's rating questions.
        var ownerCard = (await owner.PostAsync(url, Submission("interview", "StrongYes", InterviewRatings(4, 5, 5)))).Expect(HttpStatusCode.Created);
        await AutomationSamples.WaitForRunAsync(owner, application, "tag-review", "Completed");

        tasks.Count().Should().Be(1);
        tasks.Text("0.kind").Should().Be("Review");
        tasks.Id("0.assigneeUserId").Should().Be(recruiter.UserId);
        ownerCard.At("rating").GetDecimal().Should().Be(4.7m);

        // The assigned reviewer, before their scorecard: no rating anywhere, no other scorecard, no recommendation.
        using (new AssertionScope())
        {
            (await RatingsAsync(recruiter.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "AfterYourScorecard - x0");
            (await recruiter.GetAsync(url)).Expect(HttpStatusCode.OK).Count().Should().Be(0, "they read only their own scorecards");
            var withheldTimeline = Submitted((await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK));
            withheldTimeline.Should().ContainSingle("they still see that a scorecard was given");
            withheldTimeline.Should().OnlyContain(e => !e.Has("recommendation") && !e.Has("scorecardId"), "nor what it said");
            var withheldRun = RunsOf((await recruiter.GetAsync($"/v1/applications/{application}/automation-runs")).Expect(HttpStatusCode.OK), "tag-review");
            withheldRun.Should().ContainSingle().Which.GetProperty("facts").Has("reviewerRecommendation").Should().BeFalse();
        }

        // Everyone with no review of their own to give reads it: a member without a task, a Viewer and an API key.
        foreach (var reader in new[] { owner, bystander, viewer })
        {
            (await RatingsAsync(reader.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "Rated 4.7 x1");
            (await reader.GetAsync(url)).Count().Should().Be(1);
        }

        var key = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Export", ["scopes"] = new JsonArray("Read"),
        })).Expect(HttpStatusCode.Created).Text("token");
        using var keyClient = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = fixture.GetEndpoint("api") };
        keyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        (await RatingsAsync(path => GetWithKeyAsync(keyClient, path), job, application, candidate)).Values.Should().OnlyContain(r => r == "Rated 4.7 x1");
        (await GetWithKeyAsync(keyClient, url)).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        var ownerRun = RunsOf((await owner.GetAsync($"/v1/applications/{application}/automation-runs")).Expect(HttpStatusCode.OK), "tag-review");
        ownerRun.Should().ContainSingle().Which.GetProperty("facts").GetProperty("reviewerRecommendation").GetString().Should().Be("StrongYes");

        // The reviewer submits their own scorecard, a 2 (their scorecard's rating is 2.0): the mean of 4.67 and 2 is 3.33.
        var recruiterCard = (await recruiter.PostAsync(url, Submission("interview", "No", InterviewRatings(2)))).Expect(HttpStatusCode.Created);
        await WaitForCompletedRunsAsync(owner, application, "tag-review", 2);

        recruiterCard.At("rating").GetDecimal().Should().Be(2m);
        foreach (var reader in new[] { recruiter, owner, bystander, viewer })
        {
            (await RatingsAsync(reader.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "Rated 3.3 x2");
        }

        using (new AssertionScope())
        {
            (await recruiter.GetAsync(url)).Expect(HttpStatusCode.OK).Count().Should().Be(2);
            var revealed = Submitted((await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK));
            revealed.Select(e => e.GetProperty("recommendation").GetString()).Should().BeEquivalentTo("StrongYes", "No");
            revealed.Should().OnlyContain(e => e.Has("scorecardId"));
            var revealedRuns = RunsOf((await recruiter.GetAsync($"/v1/applications/{application}/automation-runs")).Expect(HttpStatusCode.OK), "tag-review");
            revealedRuns.Select(r => r.GetProperty("facts").GetProperty("reviewerRecommendation").GetString()).Should().BeEquivalentTo("StrongYes", "No");
        }
    }

    [Fact]
    public async Task A_review_asked_of_the_whole_team_holds_nothing_back_from_anyone()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var (job, application) = await ApplicationWithReviewAsync(owner, assignee: null);
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Id("candidate.id");
        (await owner.PostAsync(Scorecards(application), Submission("interview", "Yes", InterviewRatings(5, 5, 4)))).Expect(HttpStatusCode.Created);

        var tasks = (await recruiter.GetAsync($"/v1/applications/{application}/tasks")).Expect(HttpStatusCode.OK);

        tasks.Text("0.kind").Should().Be("Review");
        tasks.At("0.assigneeUserId").ValueKind.Should().Be(JsonValueKind.Null, "the review is for anyone on the team");
        (await RatingsAsync(recruiter.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "Rated 4.7 x1");
        (await recruiter.GetAsync(Scorecards(application))).Count().Should().Be(1);
    }

    // A review is done where its work is done (ADR-0017 decision 11): any scorecard its reviewer gives finishes it, which
    // shows them the rating, another stage's included.
    [Fact]
    public async Task Any_scorecard_its_reviewer_gives_finishes_a_review_and_shows_them_the_rating()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer", new JsonObject
        {
            ["id"] = "ask-review",
            ["name"] = "Ask for a review",
            ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "interview" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "AssignReviewer", ["assigneeUserId"] = recruiter.UserId }),
        });
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        await AutomationSamples.WaitForRunAsync(owner, application, "ask-review", "Completed");
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Id("candidate.id");
        var url = Scorecards(application);
        (await owner.PostAsync(url, Submission("interview", "Yes", InterviewRatings(5, 5, 5)))).Expect(HttpStatusCode.Created);
        (await RatingsAsync(recruiter.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "AfterYourScorecard - x0");
        (await owner.GetAsync($"/v1/applications/{application}/tasks")).At("0.completedAt").ValueKind
            .Should().Be(JsonValueKind.Null, "the owner's scorecard is not the reviewer's");

        (await recruiter.PostAsync(url, Submission("screening", "Yes", Answer("role_requirements", rating: 4)))).Expect(HttpStatusCode.Created);

        var review = (await owner.GetAsync($"/v1/applications/{application}/tasks")).Expect(HttpStatusCode.OK);
        review.Id("0.completedByUserId").Should().Be(recruiter.UserId);
        (await RatingsAsync(recruiter.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "Rated 4.5 x2");
        (await recruiter.GetAsync(url)).Expect(HttpStatusCode.OK).Count().Should().Be(2);
    }

    [Fact]
    public async Task A_review_cannot_be_ticked_off_by_hand_so_the_rating_waits_for_the_reviewer_s_scorecard()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var (job, application) = await ApplicationWithReviewAsync(owner, assignee: recruiter.UserId);
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Id("candidate.id");
        (await owner.PostAsync(Scorecards(application), Submission("interview", "Yes", InterviewRatings(3, 3, 3)))).Expect(HttpStatusCode.Created);

        var task = (await recruiter.GetAsync($"/v1/applications/{application}/tasks")).Id("0.id");
        (await recruiter.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.Conflict);

        (await RatingsAsync(recruiter.GetAsync, job, application, candidate)).Values.Should().OnlyContain(r => r == "AfterYourScorecard - x0");
    }

    [Fact]
    public async Task A_condition_on_the_rating_is_refused_on_a_template_draft_and_on_a_job_workflow_and_a_plain_condition_is_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var facts = new[] { "application.rating", "rating", "candidate.rating", "scorecard.average", "scorecards.count" };

        var outcomes = new Dictionary<string, string>();
        foreach (var fact in facts)
        {
            var created = await owner.PostAsync("/v1/templates", new JsonObject { ["name"] = $"Rating {fact}", ["definition"] = Samples.DefinitionWith(RatingRule(fact)) });
            outcomes[$"create {fact}"] = Refusal(created, "definition.workflow.automations[0].conditions.rules[0].fact");
        }

        var draft = (await owner.CreateTemplateAsync("Draft")).Id();
        var updated = await owner.PutAsync($"/v1/templates/{draft}/versions/1", new JsonObject { ["definition"] = Samples.DefinitionWith(RatingRule("application.rating")) });
        outcomes["update draft"] = Refusal(updated, "definition.workflow.automations[0].conditions.rules[0].fact");
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var current = (await owner.GetAsync($"/v1/jobs/{job}")).Expect(HttpStatusCode.OK);
        var workflow = JsonNode.Parse(current.At("workflow").GetRawText())!.AsObject();
        workflow["automations"] = new JsonArray(RatingRule("application.rating"));
        var tailored = await owner.PutAsync($"/v1/jobs/{job}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = current.At("workflowVersion").GetInt32() });
        outcomes["job workflow"] = Refusal(tailored, "workflow.automations[0].conditions.rules[0].fact");

        outcomes.Values.Should().OnlyContain(o => o == "refused with ADR-0013", string.Join("; ", outcomes.Where(o => o.Value != "refused with ADR-0013").Select(o => $"{o.Key}: {o.Value}")));
        var control = RatingRule("application.tags");
        control["conditions"]!["rules"]![0]!["operator"] = "Contains";
        control["conditions"]!["rules"]![0]!["value"] = "referral";
        (await owner.PostAsync("/v1/templates", new JsonObject { ["name"] = "Plain condition", ["definition"] = Samples.DefinitionWith(control) })).Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_integrity_result_never_changes_the_rating()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var (slug, jobId) = await IntegritySupport.OpenJobAsync(fixture, owner);
        var application = await IntegritySupport.ApplyAsync(fixture, slug, jobId, SyntheticApplicants.Bot);
        await IntegritySupport.SettledAsync(fixture, owner, application);
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Id("candidate.id");
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync(Scorecards(application), Submission("interview", "Yes", InterviewRatings(4, 5, 5)))).Expect(HttpStatusCode.Created);
        var ratingBefore = await RatingsAsync(owner.GetAsync, jobId, application, candidate);
        var integrityBefore = (await owner.GetAsync($"/v1/applications/{application}/integrity")).Expect(HttpStatusCode.OK).Body.GetRawText();

        // A new analyzer reports two more signals, which raises the AI assistance level: the assessment really changes.
        var announced = await RunProbeAnalyzersAsync(owner.OrganizationId, application);

        var integrityAfter = (await owner.GetAsync($"/v1/applications/{application}/integrity")).Expect(HttpStatusCode.OK).Body.GetRawText();
        announced.OfType<IntegrityAssessed>().Should().ContainSingle().Which.AiAssistance.Should().NotBe(AiAssistanceLevel.NotIndicated);
        integrityAfter.Should().NotBe(integrityBefore, "a new assessment was recorded");
        ratingBefore.Values.Should().OnlyContain(r => r == "Rated 4.7 x1");
        (await RatingsAsync(owner.GetAsync, jobId, application, candidate)).Should().BeEquivalentTo(ratingBefore);
    }

    // A job from the default hiring workflow with two automations on the interview stage: ask a review, and tag a decision.
    private static async Task<(Guid Job, Guid Application)> ApplicationWithReviewAsync(Tenant owner, Guid? assignee)
    {
        var assign = new JsonObject { ["type"] = "AssignReviewer" };
        if (assignee is { } userId)
        {
            assign["assigneeUserId"] = userId;
        }

        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer",
            new JsonObject
            {
                ["id"] = "ask-review",
                ["name"] = "Ask for a review",
                ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "interview" },
                ["actions"] = new JsonArray(assign),
            },
            new JsonObject
            {
                ["id"] = "tag-review",
                ["name"] = "Tag a reviewed candidate",
                ["trigger"] = new JsonObject { ["type"] = "ReviewerDecision", ["stageId"] = "interview" },
                ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "reviewed" }),
            });
        var application = await InInterviewAsync(owner, job);
        await AutomationSamples.WaitForRunAsync(owner, application, "ask-review", "Completed");
        return (job, application);
    }

    private static async Task<Guid> InInterviewAsync(Tenant owner, Guid job)
    {
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        return application;
    }

    private static Task WaitForCompletedRunsAsync(Tenant reader, Guid application, string automationId, int count) =>
        WolverineStore.WaitUntilAsync(
            async () => RunsOf((await reader.GetAsync($"/v1/applications/{application}/automation-runs")).Expect(HttpStatusCode.OK), automationId)
                .Count(r => r.GetProperty("status").GetString() == "Completed") == count,
            $"{count} completed runs of {automationId}",
            Ct);

    private static string Scorecards(Guid application) => $"/v1/applications/{application}/scorecards";

    // An automation whose only condition tests the fact against 5.
    private static JsonObject RatingRule(string fact) => new()
    {
        ["id"] = "rating-rule",
        ["name"] = "Act on the rating",
        ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "interview" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject { ["fact"] = fact, ["operator"] = "EqualTo", ["value"] = "5" }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "top-rated" }),
    };

    private static string Refusal(ApiResult result, string expectedKey)
    {
        if (result.Status != HttpStatusCode.BadRequest)
        {
            return $"status {(int)result.Status}";
        }

        var keys = result.ProblemKeys();
        return keys.SequenceEqual([expectedKey]) && result.ProblemMessage(expectedKey).Contains("ADR-0013", StringComparison.Ordinal)
            ? "refused with ADR-0013"
            : $"keys {string.Join(",", keys)}";
    }

    private static List<JsonElement> Submitted(ApiResult timeline) =>
        [.. timeline.Body.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "ScorecardSubmitted")];

    private static List<JsonElement> RunsOf(ApiResult runs, string automationId) =>
        [.. runs.Body.EnumerateArray().Where(r => r.GetProperty("automationId").GetString() == automationId)];

    // The rating as one line: state, value to one decimal ("-" for none) and how many scorecards it comes from.
    private static string Describe(JsonElement rating) =>
        $"{rating.GetProperty("state").GetString()} " +
        $"{(rating.Has("value") ? rating.GetProperty("value").GetDecimal().ToString("0.0", CultureInfo.InvariantCulture) : "-")} " +
        $"x{rating.GetProperty("scorecards").GetInt32()}";

    private static string Described(ApiResult applications, Guid id) =>
        Describe(applications.Body.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("rating"));

    // The application's rating as each read that carries it shows it.
    private static async Task<Dictionary<string, string>> RatingsAsync(Func<string, Task<ApiResult>> get, Guid job, Guid application, Guid candidate)
    {
        var detail = (await get($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
        var ofJob = (await get($"/v1/jobs/{job}/applications")).Expect(HttpStatusCode.OK);
        var all = (await get("/v1/applications")).Expect(HttpStatusCode.OK);
        var ofCandidate = (await get($"/v1/candidates/{candidate}")).Expect(HttpStatusCode.OK);
        return new()
        {
            ["application"] = Describe(detail.At("rating")),
            ["job list"] = Described(ofJob, application),
            ["all applications"] = Described(all, application),
            ["candidate"] = Described(new(HttpStatusCode.OK, ofCandidate.At("applications")), application),
        };
    }

    private static async Task<ApiResult> GetWithKeyAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var document = text.Length == 0 ? null : JsonDocument.Parse(text);
        return new ApiResult(response.StatusCode, document?.RootElement.Clone() ?? default);
    }

    // Records two new assessments in the application as the worker would, with analyzers the pipeline has not run before.
    private async Task<IReadOnlyList<object>> RunProbeAnalyzersAsync(Guid organizationId, Guid application)
    {
        await using var db = await IntegritySupport.TenantDbAsync(fixture, organizationId);
        var ran = await db.IntegrityAnalyzerRuns.AsNoTracking().Where(r => r.ApplicationId == application)
            .Select(r => new { r.Analyzer, r.AnalyzerVersion }).ToListAsync(Ct);
        IIntegrityAnalyzer[] already = [.. ran.Select(r => new ProbeAnalyzer(r.Analyzer, r.AnalyzerVersion, null))];
        var inconsistency = new ObservedSignal(SignalCatalogue.Inconsistency, SignalConfidence.Medium, [SignalEvidence.Fact("Probe", "A test observation.")]);
        var duplicate = new ObservedSignal(SignalCatalogue.NearDuplicateAnswer, SignalConfidence.High, [SignalEvidence.Fact("Probe", "A test observation.")]);
        IIntegrityAnalyzer[] added = [new ProbeAnalyzer("rating-probe-a", 1, new AnalyzerResult([inconsistency])), new ProbeAnalyzer("rating-probe-b", 1, new AnalyzerResult([duplicate]))];

        var messages = await new IntegrityPipeline(db, [.. already, .. added], TimeProvider.System).RunAsync(application, Ct);
        await db.SaveChangesAsync(Ct);
        return [.. messages.Cast<object>()];
    }

    // An analyzer with a name and version. Without a result it must never run: it stands for one that already ran.
    private sealed class ProbeAnalyzer(string name, int version, AnalyzerResult? result) : IIntegrityAnalyzer
    {
        public string Name => name;

        public int Version => version;

        public Task<AnalyzerResult> AnalyzeAsync(IntegrityContext context, CancellationToken cancellationToken) =>
            result is null
                ? Task.FromException<AnalyzerResult>(new InvalidOperationException($"{name} version {version} already ran and must not run again."))
                : Task.FromResult(result);
    }
}
