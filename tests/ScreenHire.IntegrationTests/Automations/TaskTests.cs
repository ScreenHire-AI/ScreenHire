using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// My tasks (<c>/v1/tasks</c>): the tasks automations make, for the signed-in person, for the whole team or for everyone,
/// open or done, and completing one and opening it again (Undo). Neither changes anything about the application.
/// Another organization's tasks are invisible, and a Viewer can neither complete nor reopen one.
/// </summary>
public sealed class TaskTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Mine_is_the_persons_own_tasks_team_is_nobody_in_particulars_and_everyone_is_all_of_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var application = await ApplicationWithTasksAsync(owner, teammate.UserId);

        (await owner.GetAsync("/v1/tasks")).Expect(HttpStatusCode.OK).Count().Should().Be(0, "Mine is the default, and nothing is assigned to the owner");
        var team = (await owner.GetAsync("/v1/tasks?scope=Team")).Expect(HttpStatusCode.OK);
        Notes(team).Should().Equal("Call the references.");
        team.Id("0.applicationId").Should().Be(application);
        team.Text("0.candidateName").Should().Be("Casey Morgan");
        team.Text("0.jobTitle").Should().Be("Platform Engineer");
        team.Text("0.kind").Should().Be("Task");

        Notes((await teammate.GetAsync("/v1/tasks?scope=Mine")).Expect(HttpStatusCode.OK)).Should().Equal("Review this application.");
        Notes((await teammate.GetAsync("/v1/tasks?scope=Team")).Expect(HttpStatusCode.OK)).Should().Equal("Call the references.");
        Notes((await owner.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK))
            .Should().BeEquivalentTo(["Call the references.", "Review this application."]);
    }

    [Fact]
    public async Task Completing_a_task_records_who_once_and_moves_it_to_done_without_touching_the_application()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var application = await ApplicationWithTasksAsync(owner, teammate.UserId);
        var task = (await owner.GetAsync("/v1/tasks?scope=Team")).Id("0.id");

        var completed = (await owner.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        completed.Id("completedByUserId").Should().Be(owner.UserId);
        var again = (await teammate.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        again.Id("completedByUserId").Should().Be(owner.UserId, "a done task stays as it was");
        again.Text("completedAt").Should().Be(completed.Text("completedAt"));

        (await owner.GetAsync("/v1/tasks?scope=Team")).Count().Should().Be(0);
        var done = (await owner.GetAsync("/v1/tasks?status=Completed&scope=Everyone")).Expect(HttpStatusCode.OK);
        done.Id("0.id").Should().Be(task);
        (await owner.GetAsync($"/v1/applications/{application}/tasks")).Body.EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == task).GetProperty("completedByUserId").GetGuid().Should().Be(owner.UserId);
        (await owner.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task Undo_reopens_a_done_task_once_without_touching_the_application()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var application = await ApplicationWithTasksAsync(owner, teammate.UserId);
        var task = (await owner.GetAsync("/v1/tasks?scope=Team")).Id("0.id");
        (await owner.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);

        var reopened = (await owner.PostAsync($"/v1/tasks/{task}/reopen")).Expect(HttpStatusCode.OK);
        reopened.Body.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);
        reopened.Body.GetProperty("completedByUserId").ValueKind.Should().Be(JsonValueKind.Null);
        (await teammate.PostAsync($"/v1/tasks/{task}/reopen")).Expect(HttpStatusCode.OK)
            .Body.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null, "an open task stays as it is");

        Notes((await owner.GetAsync("/v1/tasks?scope=Team")).Expect(HttpStatusCode.OK)).Should().Equal("Call the references.");
        (await owner.GetAsync("/v1/tasks?status=Completed&scope=Everyone")).Count().Should().Be(0);
        (await owner.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("applied");
    }

    [Fact]
    public async Task A_completion_read_before_another_one_saved_conflicts_instead_of_overwriting_who_finished()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await ApplicationWithTasksAsync(owner, teammate.UserId);
        var taskId = (await owner.GetAsync("/v1/tasks?scope=Team")).Id("0.id");

        // The teammate's request has read the open task when the owner's completion lands.
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var stale = await db.ApplicationTasks.SingleAsync(t => t.Id == taskId, Ct);
        (await owner.PostAsync($"/v1/tasks/{taskId}/complete")).Expect(HttpStatusCode.OK);

        stale.Complete(teammate.UserId, new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)).Should().BeTrue("this copy still reads as open");
        var save = () => db.SaveChangesAsync(Ct);
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();

        var done = (await owner.GetAsync("/v1/tasks?status=Completed&scope=Everyone")).Expect(HttpStatusCode.OK);
        done.Id("0.completedByUserId").Should().Be(owner.UserId);
    }

    [Fact]
    public async Task Another_organization_sees_and_completes_none_of_the_tasks_and_a_viewer_cannot_complete_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var teammate = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        await ApplicationWithTasksAsync(owner, teammate.UserId);
        var task = (await owner.GetAsync("/v1/tasks?scope=Team")).Id("0.id");
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        (await other.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        (await other.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.NotFound);
        (await viewer.GetAsync("/v1/tasks?scope=Team")).Expect(HttpStatusCode.OK).Count().Should().Be(1, "a viewer reads the team's tasks");
        (await viewer.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.Forbidden);
        (await owner.PostAsync($"/v1/tasks/{task}/complete")).Expect(HttpStatusCode.OK);
        (await other.PostAsync($"/v1/tasks/{task}/reopen")).Expect(HttpStatusCode.NotFound);
        (await viewer.PostAsync($"/v1/tasks/{task}/reopen")).Expect(HttpStatusCode.Forbidden);
        (await owner.PostAsync($"/v1/tasks/{task}/reopen")).Expect(HttpStatusCode.OK);

        (await owner.GetAsync("/v1/tasks?scope=Team")).Count().Should().Be(1, "the task is still open");
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<string> Notes(ApiResult tasks) => tasks.Body.EnumerateArray().Select(t => t.GetProperty("note").GetString()!);
}
