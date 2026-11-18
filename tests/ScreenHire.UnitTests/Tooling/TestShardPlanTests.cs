using System.Text.Json.Nodes;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The measurements CI's shards are cut on (<c>tests/shard-durations.json</c>) keep the limits of
/// <c>.claude/rules/test-value.md</c>, at the shard counts <c>ci.yml</c> uses: no class heavier than an average shard, no
/// test longer than an average shard's parallel tests take, at most a quarter of a project's classes unmeasured, and the
/// classes weighed as running alone are the ones the sources run alone. <c>scripts/test-shard.mjs --check</c> does the
/// arithmetic, so this holds no copy of it. Each red case edits a copy of the real file, or the real sources in memory,
/// so a check that cannot fail would show here.
/// </summary>
public sealed class TestShardPlanTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("shard-plan-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void The_repository_measurements_keep_every_limit()
    {
        var (exitCode, output, error) = Check(RepositoryDurations(), Classes());

        output.Should().BeEmpty(error);
        exitCode.Should().Be(0, error);
    }

    [Fact]
    public void The_checks_read_the_real_files()
    {
        var classes = Classes();
        classes.Should().Contain(["ScreenHire.E2E.AcceptanceJourneyTests", "ScreenHire.E2E.DashboardThemeTests", "ScreenHire.IntegrationTests.Demo.DevelopmentDemoTests"]);
        classes.Should().NotContain("ScreenHire.E2E.ProofWalk", "an abstract class runs nothing itself");
        classes.Count(type => type.StartsWith("ScreenHire.E2E.", StringComparison.Ordinal)).Should().BeGreaterThan(50);
        classes.Count(type => type.StartsWith("ScreenHire.IntegrationTests.", StringComparison.Ordinal)).Should().BeGreaterThan(100);

        var files = TestSources.RepositoryTestFiles();
        TestSources.SerialCollections(files).Should().Contain(["MarketingScreenshotsRun", "DevelopmentDemoRun", "WorkerLifecycle", "OwnStackRun"]);
        TestSources.AppHostFixtures(files).Should().Contain(["E2EFixture", "MarketingDemoFixture", "DevelopmentDemoFixture", "TaxOnFixture", "ProductionApiFixture"]);

        ShardCounts().Should().Equal(["ScreenHire.E2E=8", "ScreenHire.IntegrationTests=6"], "the shard counts come from ci.yml's matrices");
        var durations = RepositoryDurations();
        durations["seconds"]!.AsObject().Count.Should().BeGreaterThan(150);
        durations["alone"]!.AsObject().Select(entry => entry.Key).Should().Contain("ScreenHire.E2E.MarketingScreenshotsTests");
    }

    [Fact]
    public void A_class_heavier_than_an_average_shard_is_reported()
    {
        var durations = RepositoryDurations();
        durations["seconds"]!["ScreenHire.E2E.PipelineTests"] = 5000;

        Check(durations, Classes()).Output.Should().Contain("ScreenHire.E2E.PipelineTests weighs 5000 test-seconds, more than an average shard").And.Contain("Split the class.");
    }

    [Fact]
    public void A_test_longer_than_an_average_shards_parallel_tests_is_reported()
    {
        var durations = RepositoryDurations();
        durations["longest"]!["ScreenHire.E2E.PipelineTests"] = 400;

        Check(durations, Classes()).Output.Should().Contain("ScreenHire.E2E.PipelineTests has a test of 400 s").And.Contain("a shard cannot end before its longest test");
    }

    [Fact]
    public void A_class_that_runs_alone_is_not_asked_to_split()
    {
        // It boots a stack of its own, so it cannot be split: it only has to be weighed.
        var durations = RepositoryDurations();
        durations["alone"]!["ScreenHire.E2E.MarketingScreenshotsTests"] = 900;
        durations["longest"]!["ScreenHire.E2E.MarketingScreenshotsTests"] = 900;

        Check(durations, Classes()).Output.Should().NotContain("MarketingScreenshotsTests");
    }

    [Fact]
    public void Measurements_that_miss_over_a_quarter_of_a_projects_classes_are_reported_with_the_way_to_refresh_them()
    {
        var durations = RepositoryDurations();
        var measured = durations["seconds"]!.AsObject();
        foreach (var type in measured.Select(entry => entry.Key).Where(type => type.StartsWith("ScreenHire.IntegrationTests.", StringComparison.Ordinal)).Take(60).ToList())
        {
            measured.Remove(type);
        }

        Check(durations, Classes()).Output.Should()
            .Contain("test classes of ScreenHire.IntegrationTests have no measurement in tests/shard-durations.json, over a quarter")
            .And.Contain("gh run download <run-id> -p \"durations-*\"")
            .And.Contain("node scripts/test-shard.mjs --record TestResults/durations");
    }

    [Fact]
    public void A_project_with_no_measurement_or_no_class_found_is_reported()
    {
        var durations = RepositoryDurations();
        var measured = durations["seconds"]!.AsObject();
        foreach (var type in measured.Select(entry => entry.Key).Where(type => type.StartsWith("ScreenHire.E2E.", StringComparison.Ordinal)).ToList())
        {
            measured.Remove(type);
        }

        Check(durations, Classes()).Output.Should().Contain("has no measurement of a ScreenHire.E2E class");
        Check(RepositoryDurations(), ["Somewhere.Else.Tests"]).Output.Should().Contain("No test class of ScreenHire.E2E was found in the sources");
    }

    [Fact]
    public void The_classes_weighed_as_running_alone_are_the_ones_the_sources_run_alone()
    {
        TestSources.AloneProblems(WeighedAlone(), TestSources.RepositoryTestFiles()).Should().BeEmpty();
    }

    [Theory]
    // The class still boots its own AppHost, now beside the parallel tests, and is still weighed as alone.
    [InlineData("tests/ScreenHire.IntegrationTests/Billing/BillingTaxTests.cs", "[Collection<OwnStackRun>]\n", "",
        "ScreenHire.IntegrationTests.Billing.BillingTaxTests boots an AppHost of its own (TaxOnFixture) beside the parallel tests",
        "ScreenHire.IntegrationTests.Billing.BillingTaxTests is under \"alone\" in tests/shard-durations.json and is in no collection that runs alone")]
    // A fixture one step further from AppHostFixture counts too.
    [InlineData("tests/ScreenHire.E2E/MarketingScreenshotsTests.cs", "[Collection<MarketingScreenshotsRun>]\n", "",
        "ScreenHire.E2E.MarketingScreenshotsTests boots an AppHost of its own (MarketingDemoFixture) beside the parallel tests", null)]
    // A collection that no longer runs alone leaves its class weighed wrongly.
    [InlineData("tests/ScreenHire.IntegrationTests/OAuthRetentionTests.cs", "[CollectionDefinition(DisableParallelization = true)]", "[CollectionDefinition]",
        "ScreenHire.IntegrationTests.OAuthRetentionTests is under \"alone\" in tests/shard-durations.json and is in no collection that runs alone", null)]
    public void A_class_whose_source_and_weight_disagree_about_running_alone_is_reported(string path, string needle, string replacement, string expected, string? alsoExpected)
    {
        var files = TestSources.RepositoryTestFiles();
        files[path].Should().Contain(needle, "a stale needle would leave the file as it is and prove nothing");
        files[path] = files[path].Replace(needle, replacement, StringComparison.Ordinal);

        var problems = TestSources.AloneProblems(WeighedAlone(), files);

        problems.Should().Contain(problem => problem.StartsWith(expected, StringComparison.Ordinal));
        if (alsoExpected is not null)
        {
            problems.Should().Contain(problem => problem.StartsWith(alsoExpected, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_class_that_runs_alone_and_is_not_weighed_as_alone_is_reported()
    {
        var weighed = WeighedAlone().Where(type => type != "ScreenHire.IntegrationTests.Messaging.WorkerMessagingTests").ToList();

        TestSources.AloneProblems(weighed, TestSources.RepositoryTestFiles()).Should().ContainSingle()
            .Which.Should().StartWith("ScreenHire.IntegrationTests.Messaging.WorkerMessagingTests runs alone, after the parallel tests");
    }

    private static List<string> WeighedAlone() => [.. RepositoryDurations()["alone"]!.AsObject().Select(entry => entry.Key)];

    private static JsonNode RepositoryDurations() => JsonNode.Parse(File.ReadAllText(RepositoryFiles.PathTo("tests", "shard-durations.json")))!;

    /// <summary>Every test class of the two sharded projects, from the sources.</summary>
    private static List<string> Classes() => [.. TestSources.TestClasses(TestSources.RepositoryTestFiles()).Select(type => type.FullName)];

    /// <summary>The arguments that tell the script each sharded project's shard count, read from <c>ci.yml</c>'s matrices.</summary>
    private static List<string> ShardCounts()
    {
        var jobs = CiWorkflow.Jobs(CiWorkflow.RepositoryGitHubFiles()[CiWorkflow.CiPath]);
        string Shards(string id) => jobs.Single(job => job.Id == id).Shards.Single();
        return [$"ScreenHire.E2E={Shards("e2e")}", $"ScreenHire.IntegrationTests={Shards("integration")}"];
    }

    private (int ExitCode, string Output, string Error) Check(JsonNode durations, IEnumerable<string> classes)
    {
        var durationsFile = Path.Combine(folder, $"{Guid.NewGuid():N}.json");
        var classesFile = Path.Combine(folder, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(durationsFile, durations.ToJsonString());
        File.WriteAllLines(classesFile, classes);
        return NodeScript.Run("test-shard.mjs", ["--check", .. ShardCounts(), "--durations", durationsFile, "--classes", classesFile]);
    }
}
