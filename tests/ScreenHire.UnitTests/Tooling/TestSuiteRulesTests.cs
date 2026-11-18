namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The suite keeps what a scan can hold of <c>.claude/rules/test-value.md</c> (<see cref="TestSuiteRules"/>): the gate
/// selects no tests by changed files and retries none, no test is skipped or run alone, and no test sleeps for a fixed
/// time outside the helpers that name what they wait for. Each red case edits a real file in memory and runs the same
/// check the repository test runs, so a check that cannot fail would show here.
/// </summary>
public sealed class TestSuiteRulesTests
{
    private const string Ci = ".github/workflows/ci.yml";

    private const string UnitTest = "tests/ScreenHire.UnitTests/Tooling/TestShardTests.cs";

    private const string DashboardTest = "apps/dashboard/src/routes/(app)/settings/settings-home.test.ts";

    [Fact]
    public void The_repository_keeps_every_rule()
    {
        TestSuiteRules.Problems(TestSuiteRules.RepositoryRuleFiles()).Should().BeEmpty();
    }

    [Fact]
    public void The_checks_read_the_real_files()
    {
        var files = TestSuiteRules.RepositoryRuleFiles();

        files.Keys.Should().Contain(
        [
            Ci, ".github/actions/setup/action.yml", "package.json", "Directory.Packages.props", "apps/dashboard/package.json",
            "apps/dashboard/vitest.config.ts", "packages/realtime/vitest.config.ts", "tests/ScreenHire.E2E/ScreenHire.E2E.csproj",
            UnitTest, "tests/ScreenHire.E2E/ProofWalk.cs", "tests/ScreenHire.Testing/Wait.cs", DashboardTest,
            "apps/dashboard/src/test-support/quiet.ts", "apps/marketing/scripts/check-dist.test.mjs", "packages/realtime/src/client.test.ts",
        ]);
        files.Keys.Should().NotContain(path => path.Contains("node_modules", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal));
        files.Keys.Count(path => path.EndsWith(".test.ts", StringComparison.Ordinal)).Should().BeGreaterThan(200);
        files.Keys.Count(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)).Should().BeGreaterThan(300);
    }

    [Theory]
    // The gate selects no tests by what changed.
    [InlineData(Ci, "run test --shard=", "run test --changed --shard=", "`--changed` selects tests by changed files")]
    [InlineData("apps/dashboard/package.json", "\"test\": \"vitest run", "\"test\": \"vitest related src/lib/query.ts", "`vitest related` selects tests by changed files")]
    [InlineData(Ci, "--show-slowest-tests 15", "--only-changed", "`--only-changed` selects tests by changed files")]
    // And retries none.
    [InlineData(Ci, "--show-slowest-tests 15", "--retry-failed-tests 2", "`--retry-failed-tests` retries tests")]
    [InlineData("Directory.Packages.props", "<PackageVersion Include=\"xunit.v3\"", "<PackageVersion Include=\"Microsoft.Testing.Extensions.Retry\" Version=\"1.0.0\" />\n    <PackageVersion Include=\"xunit.v3\"", "`Microsoft.Testing.Extensions.Retry` retries tests")]
    [InlineData("apps/dashboard/vitest.config.ts", "test: {", "test: {\n    retry: 2,", "`retry:` sets Vitest's retry")]
    // No test is skipped, or left to run only when named.
    [InlineData(UnitTest, "    [Fact]\n    public void A_shard_with_no_class", "    [Fact(Skip = \"later\")]\n    public void A_shard_with_no_class", "`[Fact(Skip = \"later\")]` skips a test")]
    [InlineData(UnitTest, "    [Fact]\n    public void A_shard_with_no_class", "    [Fact(Explicit = true)]\n    public void A_shard_with_no_class", "`[Fact(Explicit = true)]` skips a test")]
    [InlineData(UnitTest, "        exitCode.Should().Be(1);", "        Assert.Skip(\"not on this machine\");", "`Assert.Skip(` skips a test")]
    [InlineData(DashboardTest, "  it('goes nowhere", "  it.skip('goes nowhere", "`it.skip` skips tests or runs only some")]
    [InlineData(DashboardTest, "  it('goes nowhere", "  it.only('goes nowhere", "`it.only` skips tests or runs only some")]
    [InlineData(DashboardTest, "describe(", "describe.skipIf(true)(", "`describe.skipIf` skips tests or runs only some")]
    // No test sleeps for a fixed time.
    [InlineData(UnitTest, "        exitCode.Should().Be(1);", "        await Task.Delay(500);", "`Task.Delay(` sleeps for a fixed time")]
    [InlineData("tests/ScreenHire.E2E/ProofWalk.cs", "        await SignInAsync();", "        await Page.WaitForTimeoutAsync(500);", "`WaitForTimeoutAsync(` sleeps for a fixed time")]
    [InlineData(UnitTest, "        exitCode.Should().Be(1);", "        Thread.Sleep(500);", "`Thread.Sleep(` sleeps for a fixed time")]
    [InlineData(DashboardTest, "    await nothingHappensFor(20, 'a navigation for a role with no settings page');", "    await new Promise((resolve) => setTimeout(resolve, 20));", "`setTimeout(resolve, 20)` sleeps on a timer")]
    public void A_broken_rule_is_reported_with_its_file_and_line(string path, string needle, string replacement, string expected)
    {
        var files = TestSuiteRules.RepositoryRuleFiles();
        files[path].Should().Contain(needle, "a stale needle would leave the file as it is and prove nothing");
        files[path] = files[path].Replace(needle, replacement, StringComparison.Ordinal);

        var problems = TestSuiteRules.Problems(files);

        problems.Should().Contain(problem => problem.StartsWith($"{path}:", StringComparison.Ordinal) && problem.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    // A turn of the event loop is not a wait.
    [InlineData(DashboardTest, "    await nothingHappensFor(20, 'a navigation for a role with no settings page');", "    await new Promise((resolve) => setTimeout(resolve, 0));")]
    [InlineData(DashboardTest, "    await nothingHappensFor(20, 'a navigation for a role with no settings page');", "    await new Promise((resolve) => setTimeout(resolve));")]
    // The helpers themselves hold the delays.
    [InlineData("tests/ScreenHire.Testing/Wait.cs", "return Task.Delay(window, cancellationToken);", "return Task.Delay(window + window, cancellationToken);")]
    public void What_the_rules_allow_is_not_reported(string path, string needle, string replacement)
    {
        var files = TestSuiteRules.RepositoryRuleFiles();
        files[path].Should().Contain(needle);
        files[path] = files[path].Replace(needle, replacement, StringComparison.Ordinal);

        TestSuiteRules.Problems(files).Should().BeEmpty();
    }
}
