using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A person or an API key gives the hiring team a task about an application (<c>POST /v1/applications/{id}/tasks</c>):
/// it lands in My tasks and on the timeline with whoever created it. Creating one needs Create tasks (a custom role
/// without it is refused) or a Write key; an assignee must be someone who can see the candidate; another organization's
/// application is a 404.
/// </summary>
public sealed class ApplicationTasksTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_task_lands_in_my_tasks_and_on_the_timeline_with_its_creator()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(recruiter);

        var task = (await recruiter.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "  Check the references.  " }))
            .Expect(HttpStatusCode.Created);

        task.Text("kind").Should().Be("Task");
        task.Text("note").Should().Be("Check the references.");
        task.At("assigneeUserId").ValueKind.Should().Be(JsonValueKind.Null, "no assignee means anyone on the hiring team");
        task.At("automationRunId").ValueKind.Should().Be(JsonValueKind.Null);
        var tasks = (await recruiter.GetAsync("/v1/tasks")).Expect(HttpStatusCode.OK);
        tasks.Body.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Contain(task.Id());
        var created = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "TaskCreated");
        created.GetProperty("taskId").GetGuid().Should().Be(task.Id());
        created.GetProperty("note").GetString().Should().Be("Check the references.");
        created.GetProperty("actorUserId").GetGuid().Should().Be(recruiter.UserId);
        (await recruiter.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("applied", "a task changes nothing else");
    }

    [Fact]
    public async Task A_write_key_creates_a_task_under_its_own_name_and_a_read_key_cannot()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner);
        var writeKey = await owner.CreateKeyAsync("Assistant", ["Read", "Write"]);
        using var writer = new KeyClient(fixture, writeKey.Text("token"));
        using var reader = new KeyClient(fixture, (await owner.CreateKeyAsync("Export", ["Read"])).Text("token"));

        (await writer.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "Book a call." }))
            .Expect(HttpStatusCode.Created);
        (await reader.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "Book a call." }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Read-only key");

        var created = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "TaskCreated").ToList();
        created.Should().ContainSingle();
        created[0].GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "a key acts for no person");
        created[0].GetProperty("actorApiKeyId").GetGuid().Should().Be(writeKey.Id("key.id"));
        created[0].GetProperty("actorApiKeyName").GetString().Should().Be("Assistant");
    }

    [Fact]
    public async Task A_role_without_create_tasks_and_another_organization_cannot_create_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner);
        using var interviewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        await owner.GiveRoleAsync(interviewer, await owner.CreateInterviewerAsync());
        using var other = await fixture.SignedInWithOrganizationAsync();

        (await interviewer.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "Mine" }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Not in your role");
        (await other.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "Mine" }))
            .Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync("/v1/tasks?scope=Everyone")).Count().Should().Be(0);
    }

    [Fact]
    public async Task A_task_needs_a_note_and_an_assignee_who_can_see_the_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        using var outsider = await fixture.SignedInWithOrganizationAsync();
        using var limited = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var path = $"/v1/applications/{application}/tasks";

        (await owner.PostAsync(path, new JsonObject { ["note"] = "   " })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        (await owner.PostAsync(path, new JsonObject { ["note"] = new string('a', 2001) })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        (await owner.PostAsync(path, new JsonObject { ["note"] = "Call", ["assigneeUserId"] = outsider.UserId.ToString() }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("assigneeUserId");
        (await owner.PostAsync(path, new JsonObject { ["note"] = "Call", ["assigneeUserId"] = limited.UserId.ToString() }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("assigneeUserId");

        await owner.SetHiringTeamAsync(job.Id(), await owner.MemberIdAsync(owner.UserId), await owner.MemberIdAsync(limited.UserId));
        var task = (await owner.PostAsync(path, new JsonObject { ["note"] = "Call", ["assigneeUserId"] = limited.UserId.ToString() }))
            .Expect(HttpStatusCode.Created);
        task.Id("assigneeUserId").Should().Be(limited.UserId);
        (await limited.GetAsync("/v1/tasks")).Body.EnumerateArray().Select(t => t.GetProperty("id").GetGuid())
            .Should().Contain(task.Id(), "the assignee sees the task in My tasks");
    }

    private static async Task<Guid> ApplicationAsync(Tenant recruiter)
    {
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await recruiter.PublishJobAsync(job.Id());
        return (await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
    }
}
