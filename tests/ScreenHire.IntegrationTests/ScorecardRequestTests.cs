using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Realtime;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Asking a teammate for a scorecard (<c>POST /v1/applications/{id}/scorecard-requests</c>), giving any library
/// scorecard, and tasks done where their work is done (ADR-0017 decision 11): a requested scorecard when its assignee
/// submits that scorecard for the application, a review when its assignee submits any scorecard (anyone, for a review
/// for the whole team), and never someone else's task. Neither is ticked off by hand; a to-do still is.
/// </summary>
public sealed class ScorecardRequestTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_requested_scorecard_reaches_the_teammate_holds_the_rating_back_and_is_done_when_they_submit_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await using var events = await EventsConnection.ConnectAsync(owner.Session, Ct);
        var (_, application) = await InInterviewAsync(owner);
        (await owner.PostAsync(Scorecards(application), Submission("interview", "Yes", InterviewRatings(4, 4, 4)))).Expect(HttpStatusCode.Created);

        var task = (await owner.PostAsync(Requests(application), Ask(teammate.UserId, "interview", "  Focus on the pairing exercise. ")))
            .Expect(HttpStatusCode.Created);

        task.Text("kind").Should().Be("Scorecard");
        task.Text("scorecardTemplateId").Should().Be("interview");
        task.Id("assigneeUserId").Should().Be(teammate.UserId);
        task.Text("note").Should().Be("Fill in the Interview scorecard. Focus on the pairing exercise.");
        var mine = (await teammate.GetAsync("/v1/tasks")).Expect(HttpStatusCode.OK);
        mine.Id("0.id").Should().Be(task.Id());
        mine.Text("0.scorecardTemplateId").Should().Be("interview");
        var created = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "TaskCreated");
        created.GetProperty("taskId").GetGuid().Should().Be(task.Id());
        created.GetProperty("actorUserId").GetGuid().Should().Be(owner.UserId);

        // Asked for their judgement, the teammate reads neither the rating nor the others' scorecards until they give it.
        (await teammate.GetAsync($"/v1/applications/{application}")).Text("rating.state").Should().Be("AfterYourScorecard");
        (await teammate.GetAsync(Scorecards(application))).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        (await teammate.PostAsync($"/v1/tasks/{task.Id()}/complete")).Expect(HttpStatusCode.Conflict);
        (await owner.PostAsync($"/v1/tasks/{task.Id()}/complete")).Expect(HttpStatusCode.Conflict);

        // The stage's own scorecard came from the Interview scorecard, so giving it is giving what was asked for.
        var given = (await teammate.PostAsync(Scorecards(application), Submission("interview", "No", InterviewRatings(2, 2, 2))))
            .Expect(HttpStatusCode.Created);

        given.Text("scorecardTemplateId").Should().Be("interview");
        given.Text("scorecardName").Should().Be("Interview");
        var done = (await owner.GetAsync($"/v1/applications/{application}/tasks")).Body.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == task.Id());
        done.GetProperty("completedByUserId").GetGuid().Should().Be(teammate.UserId);
        done.GetProperty("completedAt").GetDateTimeOffset().Should().Be(given.At("submittedAt").GetDateTimeOffset());
        (await events.WaitForAsync("ApplicationTaskCompleted", "taskId", task.Id(), Ct)).GetProperty("applicationId").GetGuid().Should().Be(application);
        var rating = (await teammate.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
        rating.Text("rating.state").Should().Be("Rated");
        rating.At("rating.scorecards").GetInt32().Should().Be(2);
        (await teammate.GetAsync("/v1/tasks")).Count().Should().Be(0);
    }

    [Fact]
    public async Task A_library_scorecard_is_given_at_the_current_stage_and_finishes_only_its_assignee_s_request_for_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var (_, application) = await InInterviewAsync(owner);
        var phoneScreen = (await owner.PostAsync(Requests(application), Ask(teammate.UserId, "phone-screen"))).Expect(HttpStatusCode.Created).Id();
        var interview = (await owner.PostAsync(Requests(application), Ask(teammate.UserId, "interview"))).Expect(HttpStatusCode.Created).Id();

        // Someone else's phone screen, and the teammate's own of another kind, leave the request open.
        (await owner.PostAsync(Scorecards(application), Library("phone-screen", "Yes", Answer("role_requirements", rating: 5)))).Expect(HttpStatusCode.Created);
        (await teammate.PostAsync(Scorecards(application), Library("technical-exercise", "Yes", Answer("exercise_solution", rating: 3))))
            .Expect(HttpStatusCode.Created);
        (await OpenTasksAsync(owner, application)).Should().BeEquivalentTo([phoneScreen, interview]);

        var given = (await teammate.PostAsync(Scorecards(application), Library("phone-screen", "StrongYes", Answer("role_requirements", rating: 4))))
            .Expect(HttpStatusCode.Created);

        given.Text("stageId").Should().Be("interview", "a library scorecard is given at the stage the candidate is in");
        given.Text("scorecardTemplateId").Should().Be("phone-screen");
        given.Text("scorecardName").Should().Be("Phone screen");
        (await OpenTasksAsync(owner, application)).Should().Equal(interview);
        // One per reviewer, stage and scorecard: the same again is a retry, anything else a conflict.
        (await teammate.PostAsync(Scorecards(application), Library("phone-screen", "StrongYes", Answer("role_requirements", rating: 4))))
            .Expect(HttpStatusCode.OK).Id().Should().Be(given.Id());
        (await teammate.PostAsync(Scorecards(application), Library("phone-screen", "No", Answer("role_requirements", rating: 1))))
            .Expect(HttpStatusCode.Conflict);
        // Library scorecards are people's judgements too, so their ratings count: 5 and 3 and 4.
        var rating = (await owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
        rating.At("rating.value").GetDecimal().Should().Be(4m);
        rating.At("rating.scorecards").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task A_review_finishes_with_its_reviewer_s_scorecard_a_team_review_with_anyone_s_and_a_to_do_with_none()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer", new JsonObject
        {
            ["id"] = "prepare",
            ["name"] = "Prepare the interview",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(
                new JsonObject { ["type"] = "CreateTask", ["note"] = "Call the references." },
                new JsonObject { ["type"] = "AssignReviewer", ["assigneeUserId"] = teammate.UserId },
                new JsonObject { ["type"] = "AssignReviewer" }),
        });
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/automations/prepare/run")).Expect(HttpStatusCode.Accepted);
        await AutomationSamples.WaitForRunAsync(owner, application, "prepare", "Completed");
        var tasks = (await owner.GetAsync($"/v1/applications/{application}/tasks")).Body.EnumerateArray().ToList();
        var toDo = IdOf(tasks, t => t.GetProperty("kind").GetString() == "Task");
        var theirs = IdOf(tasks, t => t.GetProperty("kind").GetString() == "Review" && t.Has("assigneeUserId"));
        var team = IdOf(tasks, t => t.GetProperty("kind").GetString() == "Review" && !t.Has("assigneeUserId"));

        // The candidate is in Applied, which has no scorecard of its own: a library scorecard is the way to review.
        (await owner.PostAsync(Scorecards(application), Library("phone-screen", "Yes", Answer("role_requirements", rating: 4)))).Expect(HttpStatusCode.Created);

        (await OpenTasksAsync(owner, application)).Should().BeEquivalentTo([toDo, theirs], "the owner's scorecard finishes the team's review, not the teammate's");
        (await owner.GetAsync($"/v1/applications/{application}/tasks")).Body.EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == team).GetProperty("completedByUserId").GetGuid().Should().Be(owner.UserId);

        (await teammate.PostAsync($"/v1/tasks/{theirs}/complete")).Expect(HttpStatusCode.Conflict);
        (await teammate.PostAsync(Scorecards(application), Library("screening", "Yes", Answer("role_requirements", rating: 3)))).Expect(HttpStatusCode.Created);

        (await OpenTasksAsync(owner, application)).Should().Equal(toDo);
        // A submitted scorecard is final, so the review it finished stays done.
        (await teammate.PostAsync($"/v1/tasks/{team}/reopen")).Expect(HttpStatusCode.Conflict);
        (await teammate.PostAsync($"/v1/tasks/{toDo}/complete")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_submission_names_a_stage_or_a_scorecard_and_the_current_stage_s_scorecard_is_the_job_s_copy()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var culture = await CreateScorecardAsync(owner, "Culture add", Rating5("values"));
        var job = await JobNamingAsync(owner, "Platform Engineer", "interview", culture);
        var application = await MoveToInterviewAsync(owner, job);
        // The library changes after the job took its copy; the job's candidates still answer the copy.
        (await owner.PutAsync($"/v1/scorecard-templates/{culture}", new JsonObject { ["name"] = "Culture add", ["questions"] = Questions(Rating5("teamwork")) }))
            .Expect(HttpStatusCode.OK);
        var archived = await CreateScorecardAsync(owner, "Old screen", Rating5("fit"));
        (await owner.PostAsync($"/v1/scorecard-templates/{archived}/archive")).Expect(HttpStatusCode.OK);
        var url = Scorecards(application);

        var both = Library(culture, "Yes", Answer("values", rating: 4));
        both["stageId"] = "interview";
        (await owner.PostAsync(url, both)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("stageId");
        var neither = Submission("interview", "Yes", Answer("values", rating: 4));
        neither.Remove("stageId");
        (await owner.PostAsync(url, neither)).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("stageId");
        (await owner.PostAsync(url, Library("not-a-scorecard", "Yes"))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("scorecardTemplateId");
        (await owner.PostAsync(url, Library(Guid.NewGuid().ToString(), "Yes"))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Contain("scorecardTemplateId");
        (await owner.PostAsync(url, Library(culture, "Yes", Answer("teamwork", rating: 4)))).Expect(HttpStatusCode.BadRequest);

        var given = (await owner.PostAsync(url, Library(culture, "Yes", Answer("values", rating: 4)))).Expect(HttpStatusCode.Created);

        given.Text("answers.0.questionId").Should().Be("values");
        // The stage's own scorecard is the same scorecard, so giving it again by stage is a conflict, not a second one.
        (await owner.PostAsync(url, Submission("interview", "No", Answer("values", rating: 1)))).Expect(HttpStatusCode.Conflict);
        // An archived scorecard leaves the pickers, but a request made before it was archived can still be answered.
        (await owner.PostAsync(url, Library(archived, "Yes", Answer("fit", rating: 3)))).Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_library_scorecard_another_stage_holds_asks_the_library_s_questions_as_the_sheet_shows_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var culture = await CreateScorecardAsync(owner, "Culture add", Rating5("values"));
        var job = await JobNamingAsync(owner, "Platform Engineer", "screening", culture);
        var application = await MoveToInterviewAsync(owner, job);
        (await owner.PutAsync($"/v1/scorecard-templates/{culture}", new JsonObject { ["name"] = "Culture add", ["questions"] = Questions(Rating5("teamwork")) }))
            .Expect(HttpStatusCode.OK);

        // Only the current stage's own copy stands in for the library: Screening's copy is not where the candidate is.
        (await owner.PostAsync(Scorecards(application), Library(culture, "Yes", Answer("values", rating: 4)))).Expect(HttpStatusCode.BadRequest);
        var given = (await owner.PostAsync(Scorecards(application), Library(culture, "Yes", Answer("teamwork", rating: 4)))).Expect(HttpStatusCode.Created);

        given.Text("stageId").Should().Be("interview");
        given.Text("answers.0.questionId").Should().Be("teamwork");
    }

    [Fact]
    public async Task A_reviewer_who_already_gave_the_stage_s_scorecard_is_not_assigned_a_review_nobody_could_finish()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = await JobNamingAsync(owner, "Platform Engineer", "interview", "interview", new JsonObject
        {
            ["id"] = "review",
            ["name"] = "Ask for a review",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "AssignReviewer", ["assigneeUserId"] = teammate.UserId }),
        });
        var application = await MoveToInterviewAsync(owner, job);
        (await teammate.PostAsync(Scorecards(application), Submission("interview", "Yes", InterviewRatings(4, 4, 4)))).Expect(HttpStatusCode.Created);

        (await owner.PostAsync($"/v1/applications/{application}/automations/review/run")).Expect(HttpStatusCode.Accepted);

        var run = await AutomationSamples.WaitForRunAsync(owner, application, "review", "Completed");
        run.GetProperty("actions")[0].GetProperty("status").GetString().Should().Be("Skipped");
        (await OpenTasksAsync(owner, application)).Should().BeEmpty("a review is done with a scorecard, and theirs for this stage is in");
    }

    [Fact]
    public async Task Only_a_teammate_who_sees_the_job_and_fills_in_scorecards_is_asked_once_for_a_scorecard_in_the_library()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var limited = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        using var outsider = await fixture.SignedInWithOrganizationAsync();
        var (job, application) = await InInterviewAsync(owner);
        var archived = await CreateScorecardAsync(owner, "Old screen", Rating5("fit"));
        (await owner.PostAsync($"/v1/scorecard-templates/{archived}/archive")).Expect(HttpStatusCode.OK);
        var url = Requests(application);

        (await owner.PostAsync(url, Ask(outsider.UserId, "interview"))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("assigneeUserId");
        (await owner.PostAsync(url, Ask(limited.UserId, "interview"))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("assigneeUserId");
        (await owner.PostAsync(url, Ask(viewer.UserId, "interview"))).Expect(HttpStatusCode.BadRequest)
            .ProblemMessage("assigneeUserId").Should().Contain("does not fill in scorecards");
        (await owner.PostAsync(url, Ask(Guid.Empty, "interview"))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("assigneeUserId");
        await owner.SetHiringTeamAsync(job, await owner.MemberIdAsync(owner.UserId), await owner.MemberIdAsync(limited.UserId));
        (await owner.PostAsync(url, Ask(limited.UserId, Guid.NewGuid().ToString()))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("scorecardTemplateId");
        (await owner.PostAsync(url, Ask(limited.UserId, archived))).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("scorecardTemplateId");
        (await owner.PostAsync(url, Ask(limited.UserId, "interview", new string('a', 501)))).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("note");
        (await viewer.PostAsync(url, Ask(limited.UserId, "interview"))).Expect(HttpStatusCode.Forbidden);
        (await outsider.PostAsync(url, Ask(outsider.UserId, "interview"))).Expect(HttpStatusCode.NotFound);

        (await owner.PostAsync(url, Ask(limited.UserId, "interview"))).Expect(HttpStatusCode.Created);

        // The same request is open already; then they give the screening scorecard, so asking for it again is pointless.
        (await owner.PostAsync(url, Ask(limited.UserId, "interview"))).Expect(HttpStatusCode.Conflict);
        (await limited.PostAsync(Scorecards(application), Library("screening", "Yes", Answer("role_requirements", rating: 4)))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync(url, Ask(limited.UserId, "screening"))).Expect(HttpStatusCode.Conflict);
    }

    private static async Task<(Guid Job, Guid Application)> InInterviewAsync(Tenant owner)
    {
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        return (job, await MoveToInterviewAsync(owner, job));
    }

    private static async Task<Guid> MoveToInterviewAsync(Tenant owner, Guid job)
    {
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        return application;
    }

    private static async Task<List<Guid>> OpenTasksAsync(Tenant reader, Guid application) =>
        [
            .. (await reader.GetAsync($"/v1/applications/{application}/tasks")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                .Where(t => !t.Has("completedAt"))
                .Select(t => t.GetProperty("id").GetGuid()),
        ];

    private static Guid IdOf(IEnumerable<JsonElement> tasks, Func<JsonElement, bool> which) => tasks.Single(which).GetProperty("id").GetGuid();

    private static JsonObject Ask(Guid assignee, string scorecard, string? note = null)
    {
        var body = new JsonObject { ["assigneeUserId"] = assignee.ToString(), ["scorecardTemplateId"] = scorecard };
        if (note is not null)
        {
            body["note"] = note;
        }

        return body;
    }

    // A library scorecard's submission: the scorecard, not a stage.
    private static JsonObject Library(string scorecard, string recommendation, params JsonObject[] answers)
    {
        var body = Submission("interview", recommendation, answers);
        body.Remove("stageId");
        body["scorecardTemplateId"] = scorecard;
        return body;
    }

    private static string Scorecards(Guid application) => $"/v1/applications/{application}/scorecards";

    private static string Requests(Guid application) => $"/v1/applications/{application}/scorecard-requests";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
