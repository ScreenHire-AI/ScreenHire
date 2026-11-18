using System.Text.Json;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// <c>scripts/ci-timings.mjs</c> is how a change to the CI workflow is measured (.claude/rules/test-value.md): seconds to
/// <c>Gate</c>, the job that finished last, each shard's test step and the steps before it, and job-seconds. A wrong
/// number here is a wrong decision about the workflow, so the arithmetic is pinned on a run written by hand: a job that
/// queued, Lighthouse outside the gate, a skipped job and a rerun.
/// </summary>
public sealed class CiTimingsTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string file = Path.GetTempFileName();

    public void Dispose() => File.Delete(file);

    [Fact]
    public void A_run_reports_its_last_job_the_steps_inside_it_and_job_seconds_without_Lighthouse()
    {
        var run = Summarize(Run(attempt: 1, startedAfter: 0), FirstAttempt()).GetProperty("runs")[0];

        run.GetProperty("toGate").GetInt32().Should().Be(405);
        var pole = run.GetProperty("pole");
        pole.GetProperty("name").GetString().Should().Be("End-to-end tests (2/2)", "Lighthouse ended later, and the gate does not wait for it");
        pole.GetProperty("queue").GetInt32().Should().Be(40);
        pole.GetProperty("seconds").GetInt32().Should().Be(360);
        pole.GetProperty("beforeTests").GetInt32().Should().Be(120);
        pole.GetProperty("testStep").GetInt32().Should().Be(220);
        run.GetProperty("jobSeconds").GetInt32().Should().Be(300 + 360 + 248 + 3, "two shards, checks and the gate: not Lighthouse, not the skipped job");
        run.GetProperty("queued").GetBoolean().Should().BeTrue("a shard waited 40 s for a runner");
        run.GetProperty("longestQueue").GetProperty("name").GetString().Should().Be("End-to-end tests (2/2)");
        run.GetProperty("jobs").EnumerateArray().Select(job => job.GetProperty("name").GetString()).Should().NotContain("Tree already tested");
    }

    [Fact]
    public void Several_runs_report_each_family_and_shard_and_leave_a_queued_run_out_of_the_seconds_to_Gate()
    {
        var all = Summarize(Run(attempt: 1, startedAfter: 0), FirstAttempt()).GetProperty("all");

        all.GetProperty("queuedRuns").GetInt32().Should().Be(1);
        all.GetProperty("toGate").ValueKind.Should().Be(JsonValueKind.Null, "the only run queued, so its total measures the account");
        var shards = all.GetProperty("families").GetProperty("End-to-end tests");
        shards.GetProperty("testStep").GetProperty("median").GetInt32().Should().Be(195);
        shards.GetProperty("beforeTests").GetProperty("max").GetInt32().Should().Be(120);
        shards.GetProperty("shards").GetProperty("2").GetProperty("median").GetInt32().Should().Be(220);
        shards.GetProperty("steps").GetProperty("End-to-end tests").GetProperty("count").GetInt32().Should().Be(2, "a step is named without its shard, so the shards' steps compare");
        shards.GetProperty("steps").TryGetProperty("Complete job", out _).Should().BeFalse("a step under 5 s is noise");
    }

    [Fact]
    public void A_rerun_counts_only_its_own_jobs_from_its_own_start()
    {
        // The second attempt reran one shard and the gate, 1000 s after the run was created. The first attempt's jobs come
        // back with their old times, and counting from the run's creation would add the wait before someone reran it.
        object[] jobs =
        [
            .. FirstAttempt(),
            Job("End-to-end tests (2/2)", created: 1000, started: 1002, completed: 1202, attempt: 2, steps: [Step("End-to-end tests (shard 2/2)", 1050, 1190)]),
            Job("Gate", created: 1202, started: 1204, completed: 1210, attempt: 2),
        ];

        var run = Summarize(Run(attempt: 2, startedAfter: 1000), jobs).GetProperty("runs")[0];

        run.GetProperty("rerun").GetBoolean().Should().BeTrue();
        run.GetProperty("toGate").GetInt32().Should().Be(210);
        run.GetProperty("jobs").GetArrayLength().Should().Be(2);
        run.GetProperty("jobSeconds").GetInt32().Should().Be(200 + 6);
    }

    [Fact]
    public void The_text_says_which_run_queued_and_what_finished_last()
    {
        WriteRuns(Run(attempt: 1, startedAfter: 0), FirstAttempt());

        var (exitCode, output, error) = NodeScript.Run("ci-timings.mjs", ["--file", file]);

        exitCode.Should().Be(0, error);
        output.Should().Contain("405 s to Gate").And.Contain("QUEUED: End-to-end tests (2/2) waited 40 s")
            .And.Contain("last to finish: End-to-end tests (2/2), 360 s after 40 s queued, 120 s before its tests, 220 s of tests");
    }

    private static object[] FirstAttempt() =>
    [
        Job("End-to-end tests (1/2)", created: 0, started: 5, completed: 305, steps:
        [
            Step("Set up the toolchain", 5, 45), Step("Image pulls, frontend builds and the test build, side by side", 45, 125),
            Step("End-to-end tests (shard 1/2)", 125, 295), Step("Complete job", 295, 296),
        ]),
        Job("End-to-end tests (2/2)", created: 0, started: 40, completed: 400, steps: [Step("Set up the toolchain", 40, 80), Step("End-to-end tests (shard 2/2)", 160, 380)]),
        Job("Build, contract, checks, unit tests", created: 0, started: 2, completed: 250),
        Job("Lighthouse (report-only)", created: 0, started: 3, completed: 900),
        Job("Tree already tested", created: 0, started: 0, completed: 0, conclusion: "skipped"),
        Job("Gate", created: 400, started: 402, completed: 405),
    ];

    private static object Run(int attempt, int startedAfter) => new
    {
        id = 1,
        @event = "pull_request",
        created_at = Created,
        run_started_at = Created.AddSeconds(startedAfter),
        run_attempt = attempt,
    };

    private static object Job(string name, int created, int started, int completed, int attempt = 1, string conclusion = "success", object[]? steps = null) => new
    {
        name,
        conclusion,
        run_attempt = attempt,
        created_at = Created.AddSeconds(created),
        started_at = Created.AddSeconds(started),
        completed_at = Created.AddSeconds(completed),
        steps = steps ?? [],
    };

    private static object Step(string name, int started, int completed) =>
        new { name, conclusion = "success", started_at = Created.AddSeconds(started), completed_at = Created.AddSeconds(completed) };

    private void WriteRuns(object run, object[] jobs) => File.WriteAllText(file, JsonSerializer.Serialize(new[] { new { run, jobs } }));

    private JsonElement Summarize(object run, object[] jobs)
    {
        WriteRuns(run, jobs);
        var (exitCode, output, error) = NodeScript.Run("ci-timings.mjs", ["--file", file, "--json"]);
        exitCode.Should().Be(0, error);
        return JsonDocument.Parse(output).RootElement.Clone();
    }
}
