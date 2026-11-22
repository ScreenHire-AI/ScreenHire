using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Scheduling;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The tools an assistant uses for the team's day to day work at <c>/v1/mcp</c> (docs/architecture/mcp-server.md): who is
/// on the team, scorecards, tasks, email, shared views and interviews. Each runs the route's own rules, so these tests show
/// the wiring and what is particular to a key: no email addresses for members, the key recorded as the actor, a view
/// that filters on verification refused, and a stage's scheduling defaults used when the call names none.
/// </summary>
public sealed class McpWorkToolsTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_team_members_gives_names_and_user_ids_and_no_email_address()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", name: "Robin Teammate");
        await using var mcp = await ConnectAsync(owner);

        var team = await mcp.CallAsync("list_team_members", new());

        var members = team.GetProperty("members").EnumerateArray().ToList();
        members.Select(m => m.GetProperty("userId").GetGuid()).Should().BeEquivalentTo([owner.UserId, recruiter.UserId]);
        members.Single(m => m.GetProperty("userId").GetGuid() == recruiter.UserId).GetProperty("name").GetString().Should().Be("Robin Teammate");
        members.Should().OnlyContain(m => m.GetProperty("role").ValueKind == JsonValueKind.String);
        team.GetRawText().Should().NotContain("@", "a member's address is not an assistant's to read");
    }

    [Fact]
    public async Task Request_scorecard_asks_a_teammate_and_get_scorecards_returns_theirs_once_they_submit()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer");
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        await using var mcp = await ConnectAsync(owner);

        var task = await mcp.CallAsync("request_scorecard", new()
        {
            ["applicationId"] = application, ["reviewerUserId"] = recruiter.UserId, ["scorecardTemplateId"] = "interview", ["note"] = "Please cover the system design round.",
        });
        var before = await mcp.CallAsync("get_scorecards", new() { ["applicationId"] = application });
        (await recruiter.PostAsync($"/v1/applications/{application}/scorecards",
            ScorecardSupport.Submission("interview", "Yes", ScorecardSupport.Answer("role_skills", rating: 4)))).Expect(HttpStatusCode.Created);
        var after = await mcp.CallAsync("get_scorecards", new() { ["applicationId"] = application });

        task.GetProperty("assigneeUserId").GetGuid().Should().Be(recruiter.UserId);
        before.GetProperty("scorecards").GetArrayLength().Should().Be(0);
        var scorecard = after.GetProperty("scorecards").EnumerateArray().Should().ContainSingle().Subject;
        scorecard.GetProperty("reviewerUserId").GetGuid().Should().Be(recruiter.UserId);
        scorecard.GetProperty("recommendation").GetString().Should().Be("Yes");
        scorecard.GetProperty("stageId").GetString().Should().Be("interview");
    }

    [Fact]
    public async Task Complete_task_marks_it_done_and_records_the_key_that_did()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        var key = await owner.CreateKeyAsync("Assistant", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, key.Text("token"));
        var task = (await mcp.CallAsync("create_task", new() { ["applicationId"] = application, ["note"] = "Call the references." })).GetProperty("id").GetGuid();

        var open = await mcp.CallAsync("list_tasks", new() { ["applicationId"] = application });
        var done = await mcp.CallAsync("complete_task", new() { ["taskId"] = task });
        var completed = await mcp.CallAsync("list_tasks", new() { ["applicationId"] = application, ["status"] = "Completed" });

        open.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Equal(task);
        done.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.String);
        completed.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Equal(task);
        await using var db = await fixture.ContextAsync(owner.OrganizationId, Ct);
        var row = await db.ApplicationTasks.AsNoTracking().SingleAsync(t => t.Id == task, Ct);
        row.CompletedByApiKeyId.Should().Be(key.Id("key.id"));
        row.CompletedByUserId.Should().BeNull("a key acts for no person");
        (await mcp.CallFailingAsync("complete_task", new() { ["taskId"] = Guid.CreateVersion7() })).Should().Contain("No task with that id");
    }

    [Fact]
    public async Task Draft_email_sends_nothing_and_the_confirmed_email_is_in_the_thread_as_it_was_sent()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var address = ApiSession.NewEmail("candidate");
        var application = (await owner.CreateApplicationAsync(job, address, "Casey Morgan")).Id();
        await using var mcp = await ConnectAsync(owner);
        const string Subject = "Hello {{ candidateName }}";
        const string Body = "We would like to talk to you about {{ jobTitle }}.";

        var templates = await mcp.CallAsync("list_email_templates", new());
        var draft = await mcp.CallAsync("draft_email", new() { ["applicationId"] = application, ["subject"] = Subject, ["body"] = Body });
        var emptyThread = await mcp.CallAsync("read_email_thread", new() { ["applicationId"] = application });
        await mcp.CallAsync("send_email", new() { ["applicationId"] = application, ["subject"] = Subject, ["body"] = Body, ["confirm"] = true });
        JsonElement thread = default;
        await Wait.UntilAsync(
            async () =>
            {
                thread = await mcp.CallAsync("read_email_thread", new() { ["applicationId"] = application });
                return thread.GetProperty("emails").GetArrayLength() > 0;
            },
            "the sent email in the application's thread",
            Ct);

        templates.GetProperty("sendingEnabled").GetBoolean().Should().BeTrue();
        templates.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("id").GetString()).Should().Contain("stage-change");
        draft.GetProperty("subject").GetString().Should().Contain("Casey").And.NotContain("{{");
        draft.GetProperty("text").GetString().Should().Contain("Platform Engineer").And.NotContain("{{");
        emptyThread.GetProperty("emails").GetArrayLength().Should().Be(0, "a draft is not an email");
        var sent = thread.GetProperty("emails").EnumerateArray().Should().ContainSingle().Subject;
        sent.GetProperty("direction").GetString().Should().Be("Sent");
        sent.GetProperty("subject").GetString().Should().Be(draft.GetProperty("subject").GetString());
        sent.GetProperty("text").GetString().Should().Contain("Platform Engineer");
        (await fixture.Mailpit.WaitForMessagesAsync(address, 1, Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_shared_view_with_a_tag_filter_applies_and_one_that_filters_on_verification_is_listed_but_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        await owner.PublishJobAsync(job);
        var tagged = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Casey Morgan")).Id();
        await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"), "Robin Other");
        (await owner.PostAsync($"/v1/applications/{tagged}/tags", new JsonObject { ["tag"] = "referral" })).Expect(HttpStatusCode.OK);
        var tagView = (await owner.PostAsync("/v1/views", View("Referrals", tag: "referral"))).Expect(HttpStatusCode.Created).Id();
        var verificationView = (await owner.PostAsync("/v1/views", View("Worth a closer look", authenticity: "VerificationRecommended"))).Expect(HttpStatusCode.Created).Id();
        await using var mcp = await ConnectAsync(owner);

        var views = (await mcp.CallAsync("list_views", new())).GetProperty("views").EnumerateArray().ToList();
        var applied = await mcp.CallAsync("apply_view", new() { ["viewId"] = tagView });
        var refused = await mcp.CallFailingAsync("apply_view", new() { ["viewId"] = verificationView });

        var tagListed = views.Single(v => v.GetProperty("id").GetGuid() == tagView);
        tagListed.GetProperty("canApply").GetBoolean().Should().BeTrue();
        var verificationListed = views.Single(v => v.GetProperty("id").GetGuid() == verificationView);
        verificationListed.GetProperty("canApply").GetBoolean().Should().BeFalse();
        verificationListed.GetProperty("why").GetString().Should().Contain("verification");
        applied.GetProperty("applications").EnumerateArray().Select(a => a.GetProperty("applicationId").GetGuid()).Should().Equal(tagged);
        refused.Should().Contain("verification");
    }

    [Fact]
    public async Task Invite_to_schedule_asks_the_person_then_offers_an_interview_and_records_the_key()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        var key = await world.Owner.CreateKeyAsync("Assistant", ["Read", "Write"]);
        await using var mcp = await McpSupport.ConnectAsync(fixture, key.Text("token"));
        Dictionary<string, object?> Invite(bool confirm) => new()
        {
            ["applicationId"] = world.ApplicationId, ["timeZone"] = "Europe/London", ["confirm"] = confirm, ["option"] = "OurTimes",
            ["interviewerUserIds"] = new[] { world.Owner.UserId, world.Second.UserId }, ["durationMinutes"] = 30,
        };

        var refused = await mcp.CallFailingAsync("invite_to_schedule", Invite(confirm: false));
        (await world.InterviewsAsync()).Should().BeEmpty("a refused invitation offers nothing");
        var offered = await mcp.CallAsync("invite_to_schedule", Invite(confirm: true));

        refused.Should().Contain("Not done yet");
        offered.GetProperty("status").GetString().Should().Be("Open");
        offered.GetProperty("durationMinutes").GetInt32().Should().Be(30);
        offered.GetProperty("interviewers").EnumerateArray().Select(i => i.GetProperty("userId").GetGuid()).Should().Equal(world.Owner.UserId, world.Second.UserId);
        (await world.WaitForEmailAsync(world.CandidateEmail, "Pick a time")).Should().NotBeNull();
        await world.WaitForTimelineAsync("InterviewInvited");
        var invited = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}/timeline")).Body.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "InterviewInvited");
        invited.GetProperty("actorApiKeyId").GetGuid().Should().Be(key.Id("key.id"));
        invited.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invite_to_schedule_uses_the_stages_defaults_and_the_interview_is_listed_once_the_candidate_books()
    {
        using var world = await InterviewWorld.CreateAsync(fixture);
        await using var mcp = await ConnectAsync(world.Owner);
        var read = await mcp.CallAsync("get_job", new() { ["jobId"] = world.JobId });
        var stages = JsonNode.Parse(read.GetProperty("stages").GetRawText())!.AsArray();
        stages.Single(s => s!["id"]!.GetValue<string>() == "interview")!["scheduling"] = new JsonObject
        {
            ["option"] = "OurTimes",
            ["interviewerUserIds"] = new JsonArray(JsonValue.Create(world.Owner.UserId), JsonValue.Create(world.Second.UserId)),
            ["durationMinutes"] = 45,
        };
        await mcp.CallAsync("set_job_pipeline", new()
        {
            ["jobId"] = world.JobId, ["stages"] = stages, ["expectedWorkflowVersion"] = read.GetProperty("workflowVersion").GetInt32(),
        });
        (await world.Owner.PostAsync($"/v1/applications/{world.ApplicationId}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);

        var offered = await mcp.CallAsync("invite_to_schedule", new() { ["applicationId"] = world.ApplicationId, ["timeZone"] = "UTC", ["confirm"] = true });
        var token = InterviewWorld.TokenIn((await world.WaitForEmailAsync(world.CandidateEmail, "Pick a time")).Text);
        var slot = InterviewWorld.SlotsOf((await world.PublicGetAsync(token)).Expect(HttpStatusCode.OK)).First(s => s >= DateTimeOffset.UtcNow.AddDays(2));
        (await world.BookAsync(token, slot)).Expect(HttpStatusCode.NoContent);
        JsonElement upcoming = default;
        await Wait.UntilAsync(
            async () =>
            {
                upcoming = await mcp.CallAsync("list_upcoming_interviews", new() { ["jobId"] = world.JobId });
                return upcoming.GetProperty("interviews").GetArrayLength() > 0;
            },
            "the booked interview in the upcoming list",
            Ct);

        offered.GetProperty("durationMinutes").GetInt32().Should().Be(45, "the stage's usual length");
        offered.GetProperty("interviewers").EnumerateArray().Select(i => i.GetProperty("userId").GetGuid()).Should().Equal(world.Owner.UserId, world.Second.UserId);
        var interview = upcoming.GetProperty("interviews").EnumerateArray().Should().ContainSingle().Subject;
        interview.GetProperty("interviewId").GetGuid().Should().Be(offered.GetProperty("id").GetGuid());
        interview.GetProperty("startsAt").GetDateTimeOffset().Should().Be(slot);
        interview.GetProperty("durationMinutes").GetInt32().Should().Be(45);
        interview.GetProperty("interviewerUserIds").EnumerateArray().Select(i => i.GetGuid()).Should().BeEquivalentTo([world.Owner.UserId, world.Second.UserId]);
    }

    private async Task<McpClient> ConnectAsync(Tenant owner) =>
        await McpSupport.ConnectAsync(fixture, (await owner.CreateKeyAsync("Assistant", ["Read", "Write"])).Text("token"));

    private static JsonObject View(string name, string? tag = null, string? authenticity = null) => new()
    {
        ["name"] = name,
        ["scope"] = "Candidates",
        ["jobId"] = null,
        ["shared"] = true,
        ["filters"] = new JsonObject
        {
            ["baseView"] = null,
            ["jobIds"] = new JsonArray(),
            ["stages"] = new JsonArray(),
            ["authenticity"] = authenticity,
            ["aiAssistance"] = null,
            ["tag"] = tag,
            ["sortColumn"] = null,
            ["sortDescending"] = false,
        },
    };
}
