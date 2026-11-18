namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// <c>scripts/ci-together.mjs</c> runs a CI shard's preparation side by side: the image pulls, the frontend builds and
/// the test build. A command that fails must fail the step, whichever it is, and its log must be there to read, or a
/// shard would go on to test a build that did not finish.
/// </summary>
public sealed class CiTogetherTests
{
    private const string Prints = "node -e \"console.log('built the frontends')\"";

    private const string Fails = "node -e \"console.error('no such image');process.exit(3)\"";

    [Fact]
    public void Every_command_runs_and_each_ones_log_and_seconds_are_printed()
    {
        var (exitCode, output, error) = Run("Pull the images", "node -e \"console.log('pulled 5 images')\"", "Frontend builds", Prints, "Build the tests", "node -e \"console.log('compiled')\"");

        exitCode.Should().Be(0, error);
        // The last command prints as it runs, in its group; the others follow, each in a group of its own.
        output.ReplaceLineEndings("\n").Should().Contain("::group::Build the tests\ncompiled\n::endgroup::")
            .And.Contain("::group::Pull the images\npulled 5 images\n::endgroup::")
            .And.Contain("::group::Frontend builds\nbuilt the frontends\n::endgroup::");
        output.Should().MatchRegex(@"passed: Pull the images, \d+ s").And.MatchRegex(@"passed: Frontend builds, \d+ s").And.MatchRegex(@"passed: Build the tests, \d+ s");
    }

    [Fact]
    public void A_command_that_fails_while_another_prints_fails_the_step_and_shows_its_log()
    {
        var (exitCode, output, _) = Run("Pull the images", Fails, "Build the tests", Prints);

        exitCode.Should().Be(1);
        output.ReplaceLineEndings("\n").Should().Contain("::group::Pull the images\nno such image\n::endgroup::")
            .And.Contain("FAILED: Pull the images").And.Contain("(exit 3)")
            .And.Contain("passed: Build the tests", "the other command still ran to its end, so one run shows every failure");
    }

    [Fact]
    public void The_command_that_prints_as_it_runs_fails_the_step_too()
    {
        var (exitCode, output, _) = Run("Pull the images", Prints, "Build the tests", Fails);

        exitCode.Should().Be(1);
        output.Should().Contain("FAILED: Build the tests").And.Contain("passed: Pull the images");
    }

    [Theory]
    [InlineData("Build the tests")]
    [InlineData("Pull the images", "docker pull", "Build the tests")]
    public void Anything_but_pairs_of_a_name_and_a_command_is_refused(params string[] arguments)
    {
        var (exitCode, output, error) = Run(arguments);

        exitCode.Should().Be(2);
        output.Should().BeEmpty();
        error.Should().Contain("usage: node scripts/ci-together.mjs");
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] arguments) => NodeScript.Run("ci-together.mjs", arguments);
}
