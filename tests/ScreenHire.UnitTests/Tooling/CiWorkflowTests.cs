using YamlDotNet.Core;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The CI workflow keeps its rules (<see cref="CiWorkflow"/>): the pinned runner, a timeout on every job, a Gate that
/// needs every test job, every job answering to the job that skips a tested tree, no secrets and no self-hosted runner,
/// the AppHost's images pulled before the tests, and no more jobs started by a pull request than the account runs at
/// once. Each red case edits the real files in memory and runs the same
/// check the repository test runs, so a check that cannot fail would show here.
/// </summary>
public sealed class CiWorkflowTests
{
    [Fact]
    public void The_repository_workflow_keeps_every_rule()
    {
        CiWorkflow.Problems(CiWorkflow.RepositoryGitHubFiles(), CiWorkflow.RepositoryAppHost()).Should().BeEmpty();
    }

    [Fact]
    public void The_checks_read_the_real_files()
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        var jobs = CiWorkflow.Jobs(files[CiWorkflow.CiPath]);

        jobs.Select(job => job.Id).Should().Contain(["tested-tree", "checks", "vitest", "integration", "e2e", "infra", "lighthouse", "gate"]);
        jobs.Where(job => job.Shard.Count > 0).Select(job => job.Id).Should().Equal(["vitest", "integration", "e2e"],
            "the shard check reads the matrices");
        jobs.Should().AllSatisfy(job => job.RunsOn.Should().NotBeEmpty());
        // The count of what a pull request starts reads each job: one for checks, infra and lighthouse, a leg per shard.
        jobs.Where(job => job.Legs != 1).Select(job => (job.Id, job.Legs)).Should().Equal(
            [("vitest", 2), ("integration", 5), ("e2e", 8)], "each matrix counts once per shard");
        CiWorkflow.PullRequestJobs(files[CiWorkflow.CiPath]).Should().Be(18, "checks, 2 Vitest, 5 integration, 8 end-to-end, infra and lighthouse");
        CiWorkflow.AppHostImages(CiWorkflow.RepositoryAppHost()).Should().HaveCountGreaterThanOrEqualTo(5,
            "AppHost.cs names Postgres, Azurite, Mailpit, Dev SSO and Caddy");
        CiWorkflow.PulledImages(files[CiWorkflow.CiPath]).Should().Contain("caddy:2.10.2-alpine");
        CiWorkflow.CacheKeyFiles(files).Should().Contain(
            ["pnpm-lock.yaml", "global.json", "dotnet-tools.json", "Directory.Packages.props", "**/*.csproj"],
            "the cache check reads the setup action's keys: the NuGet key's hashFiles and setup-node's pnpm cache");
    }

    [Theory]
    [InlineData("runs-on: ubuntu-24.04\n    timeout-minutes: 15", "runs-on: ubuntu-latest\n    timeout-minutes: 15", "job 'vitest' runs on [ubuntu-latest]")]
    [InlineData("runs-on: ubuntu-24.04\n    timeout-minutes: 20", "runs-on: [self-hosted, linux]\n    timeout-minutes: 20", "job 'lighthouse' runs on a self-hosted runner")]
    [InlineData("    timeout-minutes: 20\n", "", "job 'lighthouse' has no positive timeout-minutes")]
    [InlineData("needs: [tested-tree, checks, vitest, integration, e2e, infra]", "needs: [tested-tree, checks, integration, e2e, infra]", "gate needs [tested-tree, checks, integration, e2e, infra]")]
    [InlineData("needs: [tested-tree, checks, vitest, integration, e2e, infra]", "needs: [checks, vitest, integration, e2e, infra]", "gate needs [checks, vitest, integration, e2e, infra]")]
    [InlineData("name: Vitest, dashboard (${{ matrix.shard }}/${{ matrix.shards }})\n    needs: [tested-tree]\n", "name: Vitest, dashboard (${{ matrix.shard }}/${{ matrix.shards }})\n", "job 'vitest' does not need tested-tree")]
    [InlineData("name: Azure definition (aspire publish)\n    needs: [tested-tree]\n    if: ${{ !cancelled() && needs.tested-tree.outputs.tested != 'true' }}", "name: Azure definition (aspire publish)\n    needs: [tested-tree]\n    if: ${{ !cancelled() }}", "job 'infra' must run with if: !cancelled() && needs.tested-tree.outputs.tested != 'true'")]
    [InlineData("name: Lighthouse (report-only)\n    needs: [tested-tree]\n    if: ${{ !cancelled() && needs.tested-tree.outputs.tested != 'true' }}", "name: Lighthouse (report-only)\n    needs: [tested-tree]\n    if: ${{ needs.tested-tree.outputs.tested != 'true' }}", "job 'lighthouse' must run with if: !cancelled()")]
    [InlineData("if: ${{ github.event_name == 'push' }}", "if: ${{ true }}", "the tested-tree job must run only on a push")]
    [InlineData("      pull-requests: read\n", "", "tested-tree job needs pull-requests: read")]
    [InlineData("shard: [1, 2]\n        shards: [2]", "shard: [1]\n        shards: [2]", "job 'vitest' runs shards [1] of [2]")]
    [InlineData("shard: [1, 2]\n        shards: [2]", "shard: [1, 2]", "job 'vitest' runs shards [1, 2] of []")]
    [InlineData("shard: [1, 2, 3, 4, 5, 6, 7, 8]\n        shards: [8]", "shard: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]\n        shards: [11]", "a pull request starts 21 jobs at once, over the account's 20")]
    [InlineData("shard: [1, 2]\n        shards: [2]", "include: [{ shard: 1 }, { shard: 2 }]", "job 'vitest' has a matrix this check cannot count")]
    [InlineData("if: ${{ always() }}\n    runs-on", "runs-on", "if: always()")]
    [InlineData("name: Gate\n", "name: Required\n", "not 'Gate'")]
    [InlineData("DOTNET_NOLOGO: 'true'", "DOTNET_NOLOGO: ${{ secrets.NUGET_TOKEN }}", "reads a secret")]
    [InlineData("DOTNET_NOLOGO: 'true'", "DOTNET_NOLOGO: ${{ secrets['NUGET_TOKEN'] }}", "reads a secret")]
    [InlineData("DOTNET_NOLOGO: 'true'", "DOTNET_NOLOGO: ${{ toJSON(secrets) }}", "reads a secret")]
    [InlineData("DOTNET_NOLOGO: 'true'", "secrets: inherit", "reads a secret")]
    [InlineData("caddy:2.10.2-alpine", "caddy:2.10.3-alpine", "pulls caddy:2.10.3-alpine, which AppHost.cs does not run")]
    [InlineData("    ghcr.io/navikt/mock-oauth2-server:6.1.2\n", "", "runs ghcr.io/navikt/mock-oauth2-server:6.1.2, which")]
    public void A_broken_rule_in_the_workflow_is_reported(string needle, string replacement, string expected)
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        files[CiWorkflow.CiPath] = ReplaceExactly(files[CiWorkflow.CiPath], needle, replacement);

        CiWorkflow.Problems(files, CiWorkflow.RepositoryAppHost()).Should()
            .Contain(problem => problem.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    // A file joins a cache key and the fill does not run when it changes.
    [InlineData(CiWorkflow.SetupActionPath, "'**/*.csproj') }}", "'**/*.csproj', 'Directory.Build.props') }}", "does not fill main's caches when Directory.Build.props changes")]
    [InlineData(CiWorkflow.CachesPath, "      - global.json\n", "", "does not fill main's caches when global.json changes")]
    [InlineData(CiWorkflow.CachesPath, "      - pnpm-lock.yaml\n", "", "does not fill main's caches when pnpm-lock.yaml changes")]
    [InlineData(CiWorkflow.CachesPath, "      - global.json\n", "      - global.json\n      - docs/**\n", "fills main's caches when docs/** changes, and no cache key is made from it")]
    [InlineData(CiWorkflow.CachesPath, "  workflow_dispatch:", "  workflow_dispatch:\n  pull_request:", "must not run on a pull request (pull_request)")]
    [InlineData(CiWorkflow.CachesPath, "uses: ./.github/actions/setup", "uses: actions/setup-dotnet@v6", "must set up through .github/actions/setup")]
    [InlineData(CiWorkflow.CachesPath, "    branches: [main]", "    branches: [main, release]", "must run on a push to main and no other branch")]
    public void A_broken_rule_in_the_workflow_that_fills_the_caches_is_reported(string path, string needle, string replacement, string expected)
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        files[path] = ReplaceExactly(files[path], needle, replacement);

        CiWorkflow.Problems(files, CiWorkflow.RepositoryAppHost()).Should()
            .Contain(problem => problem.StartsWith(CiWorkflow.CachesPath, StringComparison.Ordinal) && problem.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_repository_with_no_workflow_to_fill_the_caches_is_reported()
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        files.Remove(CiWorkflow.CachesPath).Should().BeTrue();

        CiWorkflow.Problems(files, CiWorkflow.RepositoryAppHost()).Should().ContainSingle().Which.Should().StartWith($"{CiWorkflow.CachesPath} does not exist");
    }

    [Fact]
    public void A_secret_in_the_setup_action_is_reported()
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        const string action = ".github/actions/setup/action.yml";
        files[action] = ReplaceExactly(files[action], "run: dotnet tool restore", "run: dotnet tool restore --api-key ${{ secrets.NUGET_TOKEN }}");

        CiWorkflow.Problems(files, CiWorkflow.RepositoryAppHost()).Should().ContainSingle().Which.Should().StartWith(action);
    }

    [Theory]
    [InlineData("\"caddy\", \"2.10.2-alpine\"", "\"caddy\", \"2.11.0-alpine\"", "AppHost.cs runs caddy:2.11.0-alpine")]
    [InlineData("WithImage(\"axllent/mailpit\", \"v1.31.2\")", "WithImage(\"axllent/mailpit\", \"v1.32.0\")", "AppHost.cs runs axllent/mailpit:v1.32.0")]
    [InlineData(".WithImageRegistry(\"ghcr.io\")", ".WithImageRegistry(\"quay.io\")", "AppHost.cs runs quay.io/navikt/mock-oauth2-server:6.1.2")]
    public void An_image_the_apphost_runs_but_ci_does_not_pull_is_reported(string needle, string replacement, string expected)
    {
        var appHost = ReplaceExactly(CiWorkflow.RepositoryAppHost(), needle, replacement);

        CiWorkflow.Problems(CiWorkflow.RepositoryGitHubFiles(), appHost).Should().Contain(problem => problem.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_workflow_that_is_not_valid_yaml_fails()
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        files[CiWorkflow.CiPath] += "\n  broken: [unclosed\n";

        var check = () => CiWorkflow.Problems(files, CiWorkflow.RepositoryAppHost());

        check.Should().Throw<YamlException>();
    }

    /// <summary>
    /// Replaces every <paramref name="needle"/>, and throws when there is none: a stale needle would leave the file as it
    /// is and the red case would pass for nothing.
    /// </summary>
    private static string ReplaceExactly(string source, string needle, string replacement) =>
        source.Contains(needle, StringComparison.Ordinal)
            ? source.Replace(needle, replacement, StringComparison.Ordinal)
            : throw new InvalidOperationException($"The needle {needle} is not in the file, so this case would prove nothing.");
}
