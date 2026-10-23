using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// Scorecards (<c>/v1/applications/{id}/scorecards</c>) and the ReviewerDecision trigger they raise: a submitted
/// scorecard fires the job's reviewer-decision automations once, with the recommendation as a fact; a retry is the same
/// scorecard; another organization cannot read or submit one; a Viewer reads but cannot submit.
/// </summary>
public sealed class ScorecardTests(AppHostFixture fixture)
{
    // When an interviewer recommends yes or strong yes, tag the candidate for the next step.
    private static JsonObject AdvanceOnYes() => new()
    {
        ["id"] = "advance-on-yes",
        ["name"] = "Tag a yes from the interview",
        ["trigger"] = new JsonObject { ["type"] = "ReviewerDecision", ["stageId"] = "interview" },
        ["conditions"] = new JsonObject
        {
            ["match"] = "All",
            ["rules"] = new JsonArray(new JsonObject
            {
                ["fact"] = "reviewer.recommendation", ["operator"] = "In", ["values"] = new JsonArray("Yes", "StrongYes"),
            }),
        },
        ["actions"] = new JsonArray(new JsonObject { ["type"] = "AddTag", ["tag"] = "advance" }),
    };

    [Fact]
    public async Task A_submitted_scorecard_raises_the_reviewer_decision_once_and_a_retry_is_the_same_scorecard()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await InInterviewAsync(recruiter);
        var body = Scorecard("interview", "StrongYes", ("role_skills", "Demonstrated"), ("problem_solving", "StronglyDemonstrated"));

        var created = (await recruiter.PostAsync($"/v1/applications/{application}/scorecards", body)).Expect(HttpStatusCode.Created);
        var retried = (await recruiter.PostAsync($"/v1/applications/{application}/scorecards", body)).Expect(HttpStatusCode.OK);
        var run = await AutomationSamples.WaitForRunAsync(recruiter, application, "advance-on-yes", "Completed");

        retried.Id().Should().Be(created.Id());
        created.Id("reviewerUserId").Should().Be(recruiter.UserId);
        created.Text("recommendation").Should().Be("StrongYes");
        created.Text("ratings.0.criterionId").Should().Be("role_skills");
        created.Text("ratings.0.label").Should().Be("Skills the role needs");
        created.Text("ratings.2.criterionId").Should().Be("work_knowledge");
        created.At("ratings.2.rating").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "a criterion left out is not rated");
        run.GetProperty("eventId").GetGuid().Should().Be(created.Id("timelineEventId"));
        run.GetProperty("facts").GetProperty("reviewerRecommendation").GetString().Should().Be("StrongYes");
        (await recruiter.GetAsync($"/v1/applications/{application}")).Body.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString()).Should().Contain("advance");
        var runs = (await recruiter.GetAsync($"/v1/applications/{application}/automation-runs")).Expect(HttpStatusCode.OK).Body;
        runs.EnumerateArray().Count(r => r.GetProperty("automationId").GetString() == "advance-on-yes").Should().Be(1);
        var timeline = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body;
        var entries = timeline.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "ScorecardSubmitted").ToList();
        entries.Should().ContainSingle();
        entries[0].GetProperty("recommendation").GetString().Should().Be("StrongYes");
        entries[0].GetProperty("stageId").GetString().Should().Be("interview");
        var listed = (await recruiter.GetAsync($"/v1/applications/{application}/scorecards")).Expect(HttpStatusCode.OK);
        listed.Count().Should().Be(1);
    }

    [Fact]
    public async Task A_different_second_scorecard_from_the_same_reviewer_conflicts_and_another_reviewer_s_no_does_not_match()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(recruiter, "Recruiter");
        var application = await InInterviewAsync(recruiter);
        (await recruiter.PostAsync($"/v1/applications/{application}/scorecards", Scorecard("interview", "Yes"))).Expect(HttpStatusCode.Created);

        var changed = await recruiter.PostAsync($"/v1/applications/{application}/scorecards", Scorecard("interview", "No"));
        var second = (await teammate.PostAsync($"/v1/applications/{application}/scorecards", Scorecard("interview", "No"))).Expect(HttpStatusCode.Created);
        await AutomationSamples.WaitForRunAsync(recruiter, application, "advance-on-yes", "NotMatched");

        changed.Expect(HttpStatusCode.Conflict);
        second.Id("reviewerUserId").Should().Be(teammate.UserId);
        (await recruiter.GetAsync($"/v1/applications/{application}/scorecards")).Count().Should().Be(2);
    }

    [Fact]
    public async Task Two_identical_submissions_at_once_store_one_scorecard()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await InInterviewAsync(recruiter);
        var body = Scorecard("interview", "Yes", ("role_skills", "Demonstrated"));

        var results = await Task.WhenAll(
            recruiter.PostAsync($"/v1/applications/{application}/scorecards", body),
            recruiter.PostAsync($"/v1/applications/{application}/scorecards", body));
        await AutomationSamples.WaitForRunAsync(recruiter, application, "advance-on-yes", "Completed");

        results.Select(r => r.Status).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.OK]);
        results[0].Id().Should().Be(results[1].Id());
        (await recruiter.GetAsync($"/v1/applications/{application}/scorecards")).Count().Should().Be(1);
    }

    [Fact]
    public async Task A_scorecard_is_for_a_stage_with_one_that_the_application_has_reached_and_its_own_criteria()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Data Engineer", AdvanceOnYes());
        var application = (await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        var url = $"/v1/applications/{application}/scorecards";

        var notReached = await recruiter.PostAsync(url, Scorecard("interview", "Yes"));
        var noScorecard = await recruiter.PostAsync(url, Scorecard("applied", "Yes"));
        (await recruiter.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);
        var unknown = await recruiter.PostAsync(url, Scorecard("screening", "Yes", ("role_skills", "Demonstrated")));
        var twice = await recruiter.PostAsync(url, Scorecard("screening", "Yes", ("relevant_experience", "Demonstrated"), ("relevant_experience", "Demonstrated")));
        var badRecommendation = await recruiter.PostAsync(url, new JsonObject { ["stageId"] = "screening", ["ratings"] = new JsonArray(), ["recommendation"] = "Maybe" });

        notReached.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("stageId");
        noScorecard.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("stageId");
        unknown.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("ratings[0].criterionId");
        twice.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("ratings");
        badRecommendation.Status.Should().Be(HttpStatusCode.BadRequest);
        (await recruiter.PostAsync(url, Scorecard("screening", "No", ("relevant_experience", "PartlyDemonstrated")))).Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Another_organization_cannot_read_or_submit_a_scorecard_and_a_viewer_only_reads()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await InInterviewAsync(recruiter);
        (await recruiter.PostAsync($"/v1/applications/{application}/scorecards", Scorecard("interview", "Yes"))).Expect(HttpStatusCode.Created);
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(recruiter, "Viewer");

        (await other.GetAsync($"/v1/applications/{application}/scorecards")).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync($"/v1/applications/{application}/scorecards", Scorecard("interview", "No"))).Expect(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/v1/applications/{application}/scorecards")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        (await viewer.PostAsync($"/v1/applications/{application}/scorecards", Scorecard("interview", "No"))).Expect(HttpStatusCode.Forbidden);

        var listed = (await recruiter.GetAsync($"/v1/applications/{application}/scorecards")).Expect(HttpStatusCode.OK);
        listed.Count().Should().Be(1);
        listed.Text("0.recommendation").Should().Be("Yes");
    }

    // A job whose workflow tags a yes from the interview, and an application moved into Interview.
    private static async Task<Guid> InInterviewAsync(Tenant recruiter)
    {
        var job = await AutomationSamples.JobWithAutomationsAsync(recruiter, "Platform Engineer", AdvanceOnYes());
        var application = (await recruiter.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await recruiter.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        return application;
    }

    private static JsonObject Scorecard(string stageId, string recommendation, params (string Criterion, string Rating)[] ratings) => new()
    {
        ["stageId"] = stageId,
        ["ratings"] = new JsonArray([.. ratings.Select(r => (JsonNode)new JsonObject { ["criterionId"] = r.Criterion, ["rating"] = r.Rating })]),
        ["recommendation"] = recommendation,
    };
}
