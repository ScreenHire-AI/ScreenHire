using YamlDotNet.Core;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The CI workflow keeps its rules (<see cref="CiWorkflow"/>): the pinned runner, a timeout on every job, a Gate that
/// needs every test job, no secrets and no self-hosted runner, and the AppHost's images pulled before the tests. Each
/// red case edits the real files in memory and runs the same check the repository test runs, so a check that cannot
/// fail would show here.
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

        jobs.Select(job => job.Id).Should().Contain(["checks", "integration", "e2e", "lighthouse", "gate"]);
        jobs.Should().AllSatisfy(job => job.RunsOn.Should().NotBeEmpty());
        CiWorkflow.AppHostImages(CiWorkflow.RepositoryAppHost()).Should().HaveCountGreaterThanOrEqualTo(5,
            "AppHost.cs names Postgres, Azurite, Mailpit, Dev SSO and Caddy");
        CiWorkflow.PulledImages(files[CiWorkflow.CiPath]).Should().Contain("caddy:2.10.2-alpine");
    }

    [Theory]
    [InlineData("runs-on: ubuntu-24.04\n    timeout-minutes: 25", "runs-on: ubuntu-latest\n    timeout-minutes: 25", "job 'checks' runs on [ubuntu-latest]")]
    [InlineData("runs-on: ubuntu-24.04\n    timeout-minutes: 20", "runs-on: [self-hosted, linux]\n    timeout-minutes: 20", "job 'lighthouse' runs on a self-hosted runner")]
    [InlineData("    timeout-minutes: 20\n", "", "job 'lighthouse' has no positive timeout-minutes")]
    [InlineData("needs: [checks, integration, e2e]", "needs: [checks, integration]", "gate needs [checks, integration]")]
    [InlineData("if: ${{ always() }}\n    runs-on", "runs-on", "if: always()")]
    [InlineData("name: Gate\n", "name: Required\n", "not 'Gate'")]
    [InlineData("DOTNET_NOLOGO: 'true'", "DOTNET_NOLOGO: ${{ secrets.NUGET_TOKEN }}", "reads a secret")]
    [InlineData("DOTNET_NOLOGO: 'true'", "DOTNET_NOLOGO: ${{ secrets['NUGET_TOKEN'] }}", "reads a secret")]
    [InlineData("caddy:2.10.2-alpine", "caddy:2.10.3-alpine", "pulls caddy:2.10.3-alpine, which AppHost.cs does not run")]
    [InlineData("    ghcr.io/navikt/mock-oauth2-server:6.1.2\n", "", "runs ghcr.io/navikt/mock-oauth2-server:6.1.2, which")]
    public void A_broken_rule_in_the_workflow_is_reported(string needle, string replacement, string expected)
    {
        var files = CiWorkflow.RepositoryGitHubFiles();
        files[CiWorkflow.CiPath] = ReplaceExactly(files[CiWorkflow.CiPath], needle, replacement);

        CiWorkflow.Problems(files, CiWorkflow.RepositoryAppHost()).Should()
            .Contain(problem => problem.Contains(expected, StringComparison.Ordinal));
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
