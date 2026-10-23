using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// My tasks (<c>/v1/tasks</c>): the tasks automations make, for the signed-in person and the whole team or for everyone,
/// open or done, and completing one. Completing changes nothing about the application. Another organization's tasks
/// are invisible, and a Viewer cannot complete one.
/// </summary>
public sealed class TaskTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Mine_is_the_persons_own_tasks_and_the_teams_and_everyone_adds_teammates()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var application = await ApplicationWithTasksAsync(owner, teammate.UserId);

        var ownerMine = (await owner.GetAsync("/v1/tasks")).Expect(HttpStatusCode.OK);
        Notes(ownerMine).Should().Equal("Call the references.");
        ownerMine.Id("0.applicationId").Should().Be(application);
        ownerMine.Text("0.candidateName").Should().Be("Casey Morgan");
        ownerMine.Text("0.jobTitle").Should().Be("Platform Engineer");
        ownerMine.Text("0.kind").Should().Be("Task");

        var teammateMine = (await teammate.GetAsync("/v1/tasks?scope=Mine")).Expect(HttpStatusCode.OK);
        Notes(teammateMine).Should().BeEquivalentTo(["Call the references.", "Review this application."]);
        Notes((await owner.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK))
            .Should().BeEquivalentTo(["Call the references.", "Review this application."]);
    }

    [Fact]
    public async Task Completing_a_task_records_who_once_and_moves_it_to_done_without_touching_the_application()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var application = await ApplicationWithTasksAsync(owner, teammate.UserId);
        var task = (await owner.GetAsync("/v1/tasks")).Id("0.id");

        var completed = (await owner.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        completed.Id("completedByUserId").Should().Be(owner.UserId);
        var again = (await teammate.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        again.Id("completedByUserId").Should().Be(owner.UserId, "a done task stays as it was");
        again.Text("completedAt").Should().Be(completed.Text("completedAt"));

        (await owner.GetAsync("/v1/tasks")).Count().Should().Be(0);
        var done = (await owner.GetAsync("/v1/tasks?status=Completed")).Expect(HttpStatusCode.OK);
        done.Id("0.id").Should().Be(task);
        (await owner.GetAsync($"/v1/applications/{application}/tasks")).Body.EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == task).GetProperty("completedByUserId").GetGuid().Should().Be(owner.UserId);
        (await owner.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task Another_organization_sees_and_completes_none_of_the_tasks_and_a_viewer_cannot_complete_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await ApplicationWithTasksAsync(owner, teammate.UserId);
        var task = (await owner.GetAsync("/v1/tasks")).Id("0.id");
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        (await other.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        (await other.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.NotFound);
        (await viewer.GetAsync("/v1/tasks")).Expect(HttpStatusCode.OK).Count().Should().Be(1, "a viewer reads the team's tasks");
        (await viewer.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.Forbidden);

        (await owner.GetAsync("/v1/tasks")).Count().Should().Be(1, "the task is still open");
    }

    // A job whose Manual automation makes a team to-do and asks the teammate for a review; returns the application,
    // once both tasks exist.
    private static async Task<Guid> ApplicationWithTasksAsync(Tenant owner, Guid reviewer)
    {
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer", new JsonObject
        {
            ["id"] = "prepare",
            ["name"] = "Prepare the interview",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(
                new JsonObject { ["type"] = "CreateTask", ["note"] = "Call the references." },
                new JsonObject { ["type"] = "AssignReviewer", ["assigneeUserId"] = reviewer }),
        });
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/automations/prepare/run")).Expect(HttpStatusCode.Accepted);
        await AutomationSamples.WaitForRunAsync(owner, application, "prepare", "Completed");
        return application;
    }

    private static IEnumerable<string> Notes(ApiResult tasks) => tasks.Body.EnumerateArray().Select(t => t.GetProperty("note").GetString()!);
}
