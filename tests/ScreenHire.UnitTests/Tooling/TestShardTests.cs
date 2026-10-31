using System.Diagnostics;
using System.Text;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// <c>scripts/test-shard.mjs</c> splits a test project's classes over CI's matrix shards. Together the shards must run
/// every class exactly once, a class must never be split, and a shard left with no class must fail rather than run the
/// whole project with no filter.
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
        """;

    private readonly string listFile = Path.GetTempFileName();

    public TestShardTests() => File.WriteAllText(listFile, Listing);

    public void Dispose() => File.Delete(listFile);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_shards_together_run_every_class_once(int shards)
    {
        var classes = Enumerable.Range(1, shards).Select(shard => Run(shard, shards)).Select(Classes).ToList();

        classes.SelectMany(shard => shard).Should().BeEquivalentTo(
            ["Sample.Tests.JobsTests", "Sample.Tests.Messaging.WorkerTests", "Sample.Tests.TeamTests", "Sample.Tests.ShellTests"]);
        classes.Should().AllSatisfy(shard => shard.Should().NotBeEmpty());
    }

    [Fact]
    public void Classes_go_to_the_shard_with_the_fewest_tests_largest_first()
    {
        // JobsTests (4 tests) to shard 1; WorkerTests and TeamTests (2 each) to shard 2, which then also has 4; ShellTests
        // (1) to the first of the two lightest, shard 1.
        Classes(Run(1, 2)).Should().Equal("Sample.Tests.JobsTests", "Sample.Tests.ShellTests");
        Classes(Run(2, 2)).Should().Equal("Sample.Tests.Messaging.WorkerTests", "Sample.Tests.TeamTests");
    }

    [Fact]
    public void A_shard_with_no_class_fails_instead_of_running_everything()
    {
        var (exitCode, output, error) = Start(5, 5);

        exitCode.Should().Be(1);
        output.Should().BeEmpty();
        error.Should().Contain("Shard 5/5 has no test class");
    }

    private string Run(int shard, int shards)
    {
        var (exitCode, output, error) = Start(shard, shards);
        exitCode.Should().Be(0, error);
        return output;
    }

    /// <summary>The classes of the script's output, which is "--filter-class" and a class name, one per line, in pairs.</summary>
    private static List<string> Classes(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Where((_, index) => index % 2 == 0).Should().AllBe("--filter-class");
        return [.. lines.Where((_, index) => index % 2 == 1)];
    }

    private (int ExitCode, string Output, string Error) Start(int shard, int shards)
    {
        var start = new ProcessStartInfo("node",
            [RepositoryFiles.PathTo("scripts", "test-shard.mjs"), "--list", listFile, $"{shard}", $"{shards}"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, errorTask.Result);
    }
}
