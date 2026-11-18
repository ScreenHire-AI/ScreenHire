using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// <c>scripts/test-shard.mjs</c> splits a test project's classes over CI's matrix shards. Together the shards must run
/// every class exactly once, a class must never be split, a shard left with no class must fail rather than run the whole
/// project with no filter, and a listing with a test the script cannot place must fail rather than leave that test in no
/// shard. Its <c>--record</c> mode writes <c>tests/shard-durations.json</c> from CI's reports: the mean over the reports,
/// from each test's end and length only, with a class that runs alone weighed by its time on the clock. The reports here
/// are written by hand: a real one carries the runner's paths.
/// </summary>
public sealed class TestShardTests : IDisposable
{
    // The shape `dotnet test --list-tests` prints: a header, then each test indented by two spaces; theory rows carry
    // their arguments, which may themselves hold dots and spaces.
    private const string Listing = """
        Discovering tests from /work/tests/Sample.Tests.dll (net10.0|x64)

        Discovered 9 tests in assembly - /work/tests/Sample.Tests.dll (net10.0|x64)
          Sample.Tests.JobsTests.A_job_is_created
          Sample.Tests.JobsTests.A_job_is_closed
          Sample.Tests.JobsTests.A_job_rejects(reason: "a.b c", count: 2)
          Sample.Tests.JobsTests.A_job_rejects(reason: "x", count: 3)
          Sample.Tests.Messaging.WorkerTests.The_worker_restarts
          Sample.Tests.Messaging.WorkerTests.The_worker_handles_a_message
          Sample.Tests.TeamTests.A_member_is_invited
          Sample.Tests.TeamTests.A_member_leaves
          Sample.Tests.ShellTests.The_shell_loads

        Discovered 9 tests.
        """;

    private readonly string folder = Directory.CreateTempSubdirectory("test-shard-").FullName;

    private string ListFile => Path.Combine(folder, "tests.list");

    private string DurationsFile => Path.Combine(folder, "durations.json");

    public TestShardTests() => File.WriteAllText(ListFile, Listing);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_shards_together_run_every_class_once(int shards)
    {
        var classes = Enumerable.Range(1, shards).Select(shard => Split(shard, shards)).Select(Classes).ToList();

        classes.SelectMany(shard => shard).Should().BeEquivalentTo(
            ["Sample.Tests.JobsTests", "Sample.Tests.Messaging.WorkerTests", "Sample.Tests.TeamTests", "Sample.Tests.ShellTests"]);
        classes.Should().AllSatisfy(shard => shard.Should().NotBeEmpty());
    }

    [Fact]
    public void Classes_go_to_the_shard_with_the_fewest_tests_largest_first()
    {
        // JobsTests (4 tests) to shard 1; WorkerTests and TeamTests (2 each) to shard 2, which then also has 4; ShellTests
        // (1) to the first of the two lightest, shard 1.
        Classes(Split(1, 2)).Should().Equal("Sample.Tests.JobsTests", "Sample.Tests.ShellTests");
        Classes(Split(2, 2)).Should().Equal("Sample.Tests.Messaging.WorkerTests", "Sample.Tests.TeamTests");
    }

    [Fact]
    public void Measured_seconds_outweigh_test_counts()
    {
        // ShellTests has 1 test but 300 s measured, so it is the heaviest and gets shard 1 alone. JobsTests (20 s for 4
        // tests, 5 s each) and TeamTests (4 s for 2 tests) are measured; WorkerTests is not, so it weighs its 2 tests
        // times the median measured seconds per test (5, 2 and 300 per test: 5), 10 s.
        File.WriteAllText(DurationsFile, """
            { "parallelism": 4, "seconds": { "Sample.Tests.ShellTests": 300, "Sample.Tests.JobsTests": 20, "Sample.Tests.TeamTests": 4 } }
            """);

        Classes(Split(1, 2, DurationsFile)).Should().Equal("Sample.Tests.ShellTests");
        Classes(Split(2, 2, DurationsFile)).Should().Equal("Sample.Tests.JobsTests", "Sample.Tests.Messaging.WorkerTests", "Sample.Tests.TeamTests");
    }

    [Fact]
    public void A_class_that_runs_alone_weighs_its_seconds_on_the_clock_times_the_tests_that_run_at_once()
    {
        // WorkerTests has 2 s of tests, but it runs alone for 30 s of the shard's clock (it boots a stack of its own),
        // which is 120 test-seconds at 4 tests at once: heavier than JobsTests' 100, so it leads shard 1.
        File.WriteAllText(DurationsFile, """
            {
              "parallelism": 4,
              "seconds": { "Sample.Tests.JobsTests": 100, "Sample.Tests.Messaging.WorkerTests": 2, "Sample.Tests.TeamTests": 30, "Sample.Tests.ShellTests": 10 },
              "alone": { "Sample.Tests.Messaging.WorkerTests": 30 }
            }
            """);

        Classes(Split(1, 2, DurationsFile)).Should().Equal("Sample.Tests.Messaging.WorkerTests", "Sample.Tests.ShellTests");
        Classes(Split(2, 2, DurationsFile)).Should().Equal("Sample.Tests.JobsTests", "Sample.Tests.TeamTests");
    }

    [Fact]
    public void A_shard_with_no_class_fails_instead_of_running_everything()
    {
        var (exitCode, output, error) = Start(["--list", ListFile, "5", "5"]);

        exitCode.Should().Be(1);
        output.Should().BeEmpty();
        error.Should().Contain("Shard 5/5 has no test class");
    }

    [Theory]
    // A display name with a space is a line the script cannot read as a class and a method.
    [InlineData("  Sample.Tests.ShellTests.The_shell_loads", "  The shell loads", "discovered 9 tests, and 8 of its lines")]
    // Without the count nothing says how many tests there should be.
    [InlineData("Discovered 9 tests in assembly", "Found some tests in assembly", "no \"Discovered N tests in assembly\" line")]
    public void A_listing_with_a_test_the_script_cannot_place_fails_instead_of_leaving_it_in_no_shard(string needle, string replacement, string expected)
    {
        Listing.Should().Contain(needle);
        File.WriteAllText(ListFile, Listing.Replace(needle, replacement, StringComparison.Ordinal));

        var (exitCode, output, error) = Start(["--list", ListFile, "1", "2"]);

        exitCode.Should().Be(1);
        output.Should().BeEmpty();
        error.Should().Contain(expected);
    }

    [Fact]
    public void Measurements_in_the_old_flat_shape_fail_instead_of_weighing_nothing()
    {
        File.WriteAllText(DurationsFile, """{ "Sample.Tests.ShellTests": 300, "Sample.Tests.JobsTests": 20 }""");

        var (exitCode, _, error) = Start(["--list", ListFile, "--durations", DurationsFile, "1", "2"]);

        exitCode.Should().Be(1);
        error.Should().Contain("is not in the shape --record writes");
    }

    [Fact]
    public void Recording_takes_the_mean_over_the_reports_that_ran_a_class()
    {
        // Three runs' reports. JobsTests ran in all three (10 s, 20 s and 30 s of tests), TeamTests in two (4 s and 6 s).
        // A sum would weigh a class by how many runs were downloaded.
        WriteReport("run1/a.ctrf.json", ("Sample.Tests.JobsTests", 0, 4), ("Sample.Tests.JobsTests", 0, 6), ("Sample.Tests.TeamTests", 0, 4));
        WriteReport("run2/a.ctrf.json", ("Sample.Tests.JobsTests", 0, 20), ("Sample.Tests.TeamTests", 0, 6));
        WriteReport("run3/a.ctrf.json", ("Sample.Tests.JobsTests", 0, 30));

        var recorded = Record();

        Number(recorded, "seconds", "Sample.Tests.JobsTests").Should().Be(20);
        Number(recorded, "seconds", "Sample.Tests.TeamTests").Should().Be(5);
        Number(recorded, "longest", "Sample.Tests.JobsTests").Should().Be(18.7, "the mean of each report's longest test: 6, 20 and 30");
        recorded.GetProperty("parallelism").GetInt32().Should().Be(4);
        recorded.GetProperty("alone").EnumerateObject().Should().BeEmpty("no class was declared as running alone");
    }

    [Fact]
    public void Recording_weighs_a_class_that_runs_alone_from_the_end_of_what_ran_before_it()
    {
        // The parallel tests end at 50 s. WorkerTests is declared as running alone: its fixture boots for 40 s, then its two
        // tests run from 90 to 100 s and 100 to 112 s. It holds the shard from 50 to 112 s, 62 s, though its tests sum to 22.
        File.WriteAllText(DurationsFile, """
            { "parallelism": 4, "seconds": {}, "alone": { "Sample.Tests.Messaging.WorkerTests": 0, "Sample.Tests.DemoTests": 7 } }
            """);
        WriteReport(
            "a.ctrf.json",
            ("Sample.Tests.JobsTests", 10, 40),
            ("Sample.Tests.TeamTests", 20, 30),
            ("Sample.Tests.Messaging.WorkerTests", 90, 10),
            ("Sample.Tests.Messaging.WorkerTests", 100, 12));

        var recorded = Record();

        Number(recorded, "alone", "Sample.Tests.Messaging.WorkerTests").Should().Be(62);
        Number(recorded, "seconds", "Sample.Tests.Messaging.WorkerTests").Should().Be(22);
        Number(recorded, "alone", "Sample.Tests.DemoTests").Should().Be(7, "a class no report ran keeps its place and its number");
        recorded.GetProperty("alone").EnumerateObject().Select(entry => entry.Name).Should()
            .Equal(["Sample.Tests.DemoTests", "Sample.Tests.Messaging.WorkerTests"], "only a declared class is weighed as alone, however a report's times fall");
    }

    [Fact]
    public void Recording_reads_only_when_a_test_ended_and_how_long_it_took()
    {
        // xUnit stamps `start` when a test is queued, not when it begins: every parallel test of a run carries the same
        // one. So the same reports with every `start` rewritten must record the same numbers.
        File.WriteAllText(DurationsFile, """{ "parallelism": 4, "seconds": {}, "alone": { "Sample.Tests.Messaging.WorkerTests": 0 } }""");
        (string Type, double Begin, double Seconds)[] tests =
        [
            ("Sample.Tests.JobsTests", 10, 40), ("Sample.Tests.TeamTests", 20, 30), ("Sample.Tests.Messaging.WorkerTests", 90, 10),
        ];
        WriteReport("a.ctrf.json", tests);
        var asStamped = Record().GetRawText();

        File.WriteAllText(DurationsFile, """{ "parallelism": 4, "seconds": {}, "alone": { "Sample.Tests.Messaging.WorkerTests": 0 } }""");
        WriteReport("a.ctrf.json", start: 123_456, tests);

        Record().GetRawText().Should().Be(asStamped);
    }

    [Fact]
    public void Recording_with_no_report_fails_and_leaves_the_measurements_alone()
    {
        const string before = """{ "parallelism": 4, "seconds": { "Sample.Tests.JobsTests": 20 } }""";
        File.WriteAllText(DurationsFile, before);

        var (exitCode, _, error) = Start(["--record", Path.Combine(folder, "reports"), "--durations", DurationsFile], createReports: true);

        exitCode.Should().Be(1);
        error.Should().Contain("No CTRF report");
        File.ReadAllText(DurationsFile).Should().Be(before);
    }

    private string Split(int shard, int shards, string? durations = null)
    {
        string[] weights = durations is null ? [] : ["--durations", durations];
        var (exitCode, output, error) = Start(["--list", ListFile, .. weights, $"{shard}", $"{shards}"]);
        exitCode.Should().Be(0, error);
        return output;
    }

    /// <summary>Runs <c>--record</c> over the reports written so far and returns the measurements file it rewrote.</summary>
    private JsonElement Record()
    {
        var (exitCode, _, error) = Start(["--record", Path.Combine(folder, "reports"), "--durations", DurationsFile]);
        exitCode.Should().Be(0, error);
        return JsonDocument.Parse(File.ReadAllText(DurationsFile)).RootElement.Clone();
    }

    private static double Number(JsonElement recorded, string map, string type) => recorded.GetProperty(map).GetProperty(type).GetDouble();

    private void WriteReport(string name, params (string Type, double Begin, double Seconds)[] tests) => WriteReport(name, start: null, tests);

    /// <summary>
    /// A CTRF report as xUnit writes one, with only what the script reads: each test's suite path (the class is its
    /// second to last entry), its length and its end in milliseconds, and a `start` the script must ignore.
    /// </summary>
    private void WriteReport(string name, long? start, params (string Type, double Begin, double Seconds)[] tests)
    {
        var entries = tests.Select((test, index) => string.Create(CultureInfo.InvariantCulture, $$"""
            { "name": "{{test.Type}}.Test_{{index}}", "status": "passed", "suite": ["Sample.Tests", "{{test.Type}}", "Test_{{index}}"],
              "duration": {{test.Seconds * 1000}}, "start": {{start ?? (long)(test.Begin * 1000)}}, "stop": {{(test.Begin + test.Seconds) * 1000}} }
            """));
        var path = Path.Combine(folder, "reports", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""{ "results": { "tests": [{{string.Join(",", entries)}}] } }""");
    }

    /// <summary>The classes of the script's output, which is "--filter-class" and a class name, one per line, in pairs.</summary>
    private static List<string> Classes(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Where((_, index) => index % 2 == 0).Should().AllBe("--filter-class");
        return [.. lines.Where((_, index) => index % 2 == 1)];
    }

    private (int ExitCode, string Output, string Error) Start(string[] arguments, bool createReports = false)
    {
        if (createReports)
        {
            Directory.CreateDirectory(Path.Combine(folder, "reports"));
        }

        return NodeScript.Run("test-shard.mjs", arguments);
    }
}

/// <summary>Runs a script under <c>scripts/</c> with node and returns its exit code and what it printed.</summary>
internal static class NodeScript
{
    public static (int ExitCode, string Output, string Error) Run(string script, IEnumerable<string> arguments, string? input = null)
    {
        var start = new ProcessStartInfo("node", [RepositoryFiles.PathTo("scripts", script), .. arguments])
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, errorTask.Result);
    }
}
