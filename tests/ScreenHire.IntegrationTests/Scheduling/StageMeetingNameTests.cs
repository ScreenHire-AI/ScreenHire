using System.Text.Json.Nodes;
using ScreenHire.Application.Scheduling;

namespace ScreenHire.IntegrationTests.Scheduling;

/// <summary>
/// A stage's optional <c>meetingName</c> from the hiring definition to the candidate's words: the invitation, the
/// confirmation, the reminders and the cancellation emails, the calendar event, the candidate's page and the answer to a save that
/// names it badly. A stage that names none calls the meeting "interview".
/// </summary>
public sealed class StageMeetingNameTests(AppHostFixture fixture)
{
    private const string Interview = "interview";
    private const string TrialShift = "trial shift";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stage_that_names_its_meeting_gives_the_name_to_the_emails_the_event_and_the_page_and_the_interview_keeps_it_after_the_candidate_moves_on()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var (_, application, email) = await JobAndApplicationAsync(world, "Line Cook", "screening", TrialShift);
        await MoveAsync(world, application, "screening");

        var (interviewId, token) = await world.ScheduledAsync(application, email);

        var invitation = await world.WaitForEmailAsync(email, "Pick a time for your trial shift");
        invitation.Subject.Should().StartWith("Pick a time for your trial shift with ").And.EndWith(" about Line Cook");
        invitation.Text.Should().Contain("Next is your trial shift");
        (await world.StoredAsync(interviewId)).MeetingName.Should().Be(TrialShift);
        var offered = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        offered.Text("state").Should().Be("Open");
        offered.Text("meetingName").Should().Be(TrialShift);

        var chosen = FarSlot(InterviewWorld.SlotsOf(offered));
        (await world.BookAsync(token, chosen)).Expect(HttpStatusCode.NoContent);

        var organizerEvent = (await world.WaitForLiveEventsAsync(world.Owner.UserId, 1)).Single();
        organizerEvent.Summary.Should().Be("Trial shift: Casey Morgan - Line Cook");
        var confirmation = await world.WaitForEmailAsync(email, "is set");
        confirmation.Subject.Should().StartWith("Your trial shift with ");

        // The reminders a day before name it too: the candidate's from its template, the interviewer's from the worker's own words.
        await world.WaitForSyncAsync(interviewId, "Synced", application);
        await world.DeliverReminderAsync(interviewId, chosen, InterviewReminderKind.DayBefore, application);
        (await world.WaitForEmailAsync(email, "Reminder: your trial shift")).Subject.Should().StartWith("Reminder: your trial shift with ");
        var forInterviewer = await world.WaitForEmailAsync(await world.Owner.EmailOfAsync(), "Trial shift in a day");
        forInterviewer.Subject.Should().Be("Trial shift in a day: Casey Morgan");
        forInterviewer.Text.Should().Contain("Your trial shift with a candidate is in about a day");

        // The candidate goes on to the interview stage, which names no meeting. The interview they were invited to keeps its name.
        await MoveAsync(world, application, "interview");
        (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK).Text("meetingName").Should().Be(TrialShift);
        (await world.Owner.PostAsync($"/v1/interviews/{interviewId}/cancel")).Expect(HttpStatusCode.OK);

        var cancelled = await world.WaitForEmailAsync(email, "is cancelled");
        cancelled.Subject.Should().StartWith("Your trial shift with ").And.EndWith(" is cancelled");
        cancelled.Text.Should().Contain("trial shift");
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("state").Should().Be("Cancelled");
        page.Text("meetingName").Should().Be(TrialShift);
    }

    [Fact]
    public async Task A_stage_that_names_no_meeting_calls_it_an_interview_everywhere()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);

        var (interviewId, token) = await world.ScheduledAsync();

        var invitation = await world.WaitForEmailAsync(world.CandidateEmail, "Pick a time for your interview");
        invitation.Subject.Should().StartWith("Pick a time for your interview with ").And.EndWith(" about Platform Engineer");
        (await world.StoredAsync(interviewId)).MeetingName.Should().BeNull("a stage with no meeting name leaves the request unnamed");
        var page = (await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK);
        page.Text("meetingName").Should().Be(Interview);

        (await world.BookAsync(token, FarSlot(InterviewWorld.SlotsOf(page)))).Expect(HttpStatusCode.NoContent);
        (await world.WaitForLiveEventsAsync(world.Owner.UserId, 1)).Single().Summary.Should().Be("Interview: Casey Morgan - Platform Engineer");
        (await world.WaitForEmailAsync(world.CandidateEmail, "is set")).Subject.Should().StartWith("Your interview with ");
    }

    [Fact]
    public async Task The_schedule_interview_action_names_the_meeting_after_the_stage_the_candidate_entered()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var automation = new JsonObject
        {
            ["id"] = "schedule",
            ["name"] = "Offer a trial shift",
            ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "screening" },
            ["actions"] = new JsonArray(new JsonObject
            {
                ["type"] = "ScheduleInterview",
                ["interviewerUserIds"] = new JsonArray(JsonValue.Create(world.Owner.UserId)),
                ["interviewMinutes"] = 30,
                ["interviewWindowDays"] = 14,
                ["interviewTimeZone"] = "UTC",
            }),
        };
        var (_, application, email) = await JobAndApplicationAsync(world, "Barista", "screening", TrialShift, automation);

        await MoveAsync(world, application, "screening");

        var invitation = await world.WaitForEmailAsync(email, "Pick a time for your trial shift");
        var page = (await world.PublicGetAsync(InterviewWorld.TokenIn(invitation.Text))).Expect(HttpStatusCode.OK);
        page.Text("meetingName").Should().Be(TrialShift);
        (await world.InterviewsAsync(application)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("   ", "blank")]
    [InlineData("a very long name for a meeting that is over forty characters", "over 40 characters")]
    public async Task A_blank_or_overlong_meeting_name_is_a_problem_on_the_template_and_refused_for_an_open_jobs_workflow(string name, string because)
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var definition = Samples.Definition();
        StageOf(definition["workflow"]!.AsObject(), "interview")["meetingName"] = name;

        // A template saves as a draft with its problems, and publishing is refused.
        var template = (await recruiter.PostAsync("/v1/templates", new JsonObject { ["name"] = "Named badly", ["definition"] = definition })).Expect(HttpStatusCode.Created);
        var draft = (await recruiter.GetAsync($"/v1/templates/{template.Id()}/versions/1")).Expect(HttpStatusCode.OK);
        draft.At("problems").EnumerateArray().Select(p => p.GetProperty("path").GetString()).Should().Contain("workflow.stages[2].meetingName", because);
        (await recruiter.PostAsync($"/v1/templates/{template.Id()}/versions/1/publish")).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Contain("definition.workflow.stages[2].meetingName");

        // An open job's workflow is live, so the same name is refused outright and nothing is saved.
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Open and misnamed"));
        await recruiter.PublishJobAsync(job.Id());
        var workflow = JsonNode.Parse(job.At("workflow").GetRawText())!.AsObject();
        StageOf(workflow, "interview")["meetingName"] = name;
        var saved = await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", new JsonObject { ["workflow"] = workflow, ["expectedWorkflowVersion"] = 1 });

        saved.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Contain("workflow.stages[2].meetingName");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}")).At("workflowVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_meeting_name_saved_on_a_jobs_workflow_is_returned_and_can_be_cleared()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Named stage"));
        var workflow = JsonNode.Parse(job.At("workflow").GetRawText())!.AsObject();
        StageOf(workflow, "interview")["meetingName"] = "phone screen";

        var named = (await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", new JsonObject { ["workflow"] = workflow.DeepClone(), ["expectedWorkflowVersion"] = 1 })).Expect(HttpStatusCode.OK);
        StageOf(workflow, "interview").Remove("meetingName");
        var cleared = (await recruiter.PutAsync($"/v1/jobs/{job.Id()}/workflow", new JsonObject { ["workflow"] = workflow.DeepClone(), ["expectedWorkflowVersion"] = 2 })).Expect(HttpStatusCode.OK);

        named.Text("workflow.stages.2.meetingName").Should().Be("phone screen");
        named.At("workflowProblems").GetArrayLength().Should().Be(0);
        cleared.At("workflow.stages.2").Has("meetingName").Should().BeFalse("a stage with no name is back to \"interview\"");
    }

    private static JsonObject StageOf(JsonObject workflow, string id) =>
        workflow["stages"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!.AsObject();

    private static DateTimeOffset FarSlot(IReadOnlyList<DateTimeOffset> slots) => slots.First(s => s >= DateTimeOffset.UtcNow.AddDays(2));

    // A job whose workflow is the default one with this meeting name on the stage (a published template, then a job from it).
    private static async Task<(Guid Job, Guid Application, string Email)> JobAndApplicationAsync(
        InterviewWorld world, string title, string stageId, string meetingName, params JsonObject[] automations)
    {
        var definition = Samples.DefinitionWith(automations);
        StageOf(definition["workflow"]!.AsObject(), stageId)["meetingName"] = meetingName;
        var (_, versionId) = await world.Owner.CreatePublishedTemplateAsync($"{title} template", definition);
        var body = Samples.RemoteJob(title);
        body["templateVersionId"] = versionId;
        var job = (await world.Owner.CreateJobAsync(body)).Id();
        var email = ApiSession.NewEmail("candidate");
        return (job, (await world.Owner.CreateApplicationAsync(job, email)).Id(), email);
    }

    private static async Task MoveAsync(InterviewWorld world, Guid application, string stage) =>
        (await world.Owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = stage })).Expect(HttpStatusCode.OK);
}
