using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Application.Distribution;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.Distribution;

/// <summary>
/// A publish or close that arrives while the worker is saving a board's answer waits for that answer and succeeds; the
/// worker is not "someone else", so the recruiter never gets a 409 for it (docs/architecture/job-distribution.md). Each
/// test plays the worker itself: it holds the sandbox posting's row lock and changes the row, as a handler does around a
/// board call, until the API is seen waiting on it. Before the API took the same lock, both were a 409 every time.
/// </summary>
public sealed class BoardAnswerRaceTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Republishing_changed_content_while_a_board_answer_is_being_saved_waits_for_it_and_succeeds()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var body = Samples.RemoteJob("Answer race");
        var jobId = (await recruiter.CreateJobAsync(body)).Id();
        await recruiter.PublishJobAsync(jobId);
        await DistributionTests.WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");
        body["title"] = "Answer race, renamed";
        (await recruiter.PutAsync($"/v1/jobs/{jobId}", body)).Expect(HttpStatusCode.OK);

        var republished = await WhileABoardAnswerIsSavedAsync(recruiter, jobId, () => recruiter.PostAsync($"/v1/jobs/{jobId}/publish"));

        republished.Expect(HttpStatusCode.OK);
        Sandbox(republished).GetProperty("jobVersion").GetInt32().Should().Be(2, "the publish planned on the saved answer and sent version 2");
    }

    [Fact]
    public async Task Closing_while_a_board_answer_is_being_saved_waits_for_it_and_takes_the_job_down()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var jobId = (await recruiter.CreateJobAsync(Samples.RemoteJob("Close race"))).Id();
        await recruiter.PublishJobAsync(jobId);
        await DistributionTests.WaitForPostingAsync(recruiter, jobId, "sandbox", "Live");

        var closed = await WhileABoardAnswerIsSavedAsync(recruiter, jobId, () => recruiter.PostAsync($"/v1/jobs/{jobId}/close"));

        closed.Expect(HttpStatusCode.OK);
        closed.Text("status").Should().Be("Closed");
        Sandbox(closed).GetProperty("state").GetString().Should().BeOneOf(["Closing", "Closed"], "the live listing was asked to come down");
    }

    // Holds the sandbox posting's row lock and changes the row, as a handler saving a board's answer does, starts the
    // request, waits until Postgres shows the request blocked on that lock, then commits the answer.
    private async Task<ApiResult> WhileABoardAnswerIsSavedAsync(Tenant recruiter, Guid jobId, Func<Task<ApiResult>> request)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(recruiter.OrganizationId);
        await using var worker = await fixture.CreateDbContextAsync(tenant, Ct);
        await using var answer = await PostingLocks.BeginForJobAsync(worker, jobId, "sandbox", Ct);
        var workerPid = await worker.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Ct);
        await worker.Database.ExecuteSqlAsync(
            $"UPDATE job_postings SET updated_at = now() WHERE job_id = {jobId} AND board_id = 'sandbox'", Ct);

        var pending = request();
        await WolverineStore.WaitUntilAsync(
            async () => pending.IsCompleted || await IsBlockedByAsync(workerPid), "the request to wait on the posting's lock", Ct);
        await answer.CommitAsync(Ct);
        return await pending;
    }

    // Postgres' own view of lock waits; the owner connection only observes, it changes nothing. A handler of the real
    // worker locks one posting by id, so its lock query is left out: only the API can be the one waiting here. The filter
    // matches the SQL of PostingLocks.LockAsync; keep the two in step.
    private async Task<bool> IsBlockedByAsync(int pid)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "select count(*) from pg_stat_activity where @pid = any(pg_blocking_pids(pid)) " +
            "and query not like '%FROM job_postings WHERE id = $1 FOR UPDATE%'",
            connection);
        command.Parameters.AddWithValue("pid", pid);
        return (long)(await command.ExecuteScalarAsync(Ct))! > 0;
    }

    private static System.Text.Json.JsonElement Sandbox(ApiResult job) =>
        job.At("postings").EnumerateArray().Single(p => p.GetProperty("boardId").GetString() == "sandbox");
}
