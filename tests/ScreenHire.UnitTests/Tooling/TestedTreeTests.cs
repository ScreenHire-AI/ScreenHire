using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// <c>scripts/tested-tree.mjs</c> lets a push to <c>main</c> skip its tests. A wrong yes lands a tree nothing tested, so
/// every red case here is a way the answer must stay no: no merged pull request, a tree that differs from the PR head's
/// (a squash of a branch that was behind <c>main</c>), a check that failed or is still running, no passing <c>Gate</c>
/// from GitHub Actions, and any API error. The script is driven from <c>node</c> with a fake GitHub API.
/// </summary>
public sealed class TestedTreeTests
{
    private const string Pushed = "pushed0";
    private const string Head = "head0";

    // Imports the script by file URL (a bare Windows path is not a valid specifier), builds the API from the scenario
    // and prints the decision. Run through stdin, so process.argv[1] is unset, which the script must treat as "imported".
    private const string Driver = """
        import { pathToFileURL } from 'node:url';
        const { decideTestedTree } = await import(pathToFileURL(process.env.TESTED_TREE_SCRIPT).href);
        const scenario = JSON.parse(process.env.TESTED_TREE_SCENARIO);
        const fails = (call) => { if (scenario.fails === call) throw new Error(`${call} answered 500`); };
        const api = {
          pullsForCommit: async () => { fails('pulls'); return scenario.pulls; },
          treeOf: async (sha) => { fails('tree'); return scenario.trees[sha]; },
          checksOn: async (sha) => { fails('checks'); return scenario.checks[sha]; },
        };
        console.log(JSON.stringify(await decideTestedTree('pushed0', api)));
        """;

    // githubApi's checksOn over a stubbed fetch with 150 check runs in two pages, to prove it reads every page.
    private const string PagingDriver = """
        import { pathToFileURL } from 'node:url';
        const { githubApi } = await import(pathToFileURL(process.env.TESTED_TREE_SCRIPT).href);
        const requested = [];
        const fetchResponse = async (url) => {
          requested.push(url);
          const page = Number(new URL(url).searchParams.get('page'));
          const count = page === 1 ? 100 : 50;
          const runs = Array.from({ length: count }, (_, index) => ({ name: `check ${page}-${index}` }));
          return { ok: true, json: async () => ({ total_count: 150, check_runs: runs }) };
        };
        const checks = await githubApi('example/repo', 'token', fetchResponse).checksOn('head0');
        console.log(JSON.stringify({ count: checks.length, requested }));
        """;

    [Fact]
    public void A_push_whose_tree_passed_Gate_on_its_pull_request_is_skipped()
    {
        var decision = Decide(Scenario());

        decision.Tested.Should().BeTrue();
        decision.Reason.Should().Contain("PR #7").And.Contain(Head);
    }

    [Fact]
    public void Skipped_and_neutral_checks_beside_Gate_do_not_stop_the_skip()
    {
        // Lighthouse skips itself when the change leaves the marketing build alone.
        var checks = new[] { Check("Gate"), Check("Lighthouse (report-only)", conclusion: "skipped"), Check("Other", conclusion: "neutral") };

        Decide(Scenario(checks: checks)).Tested.Should().BeTrue();
    }

    [Fact]
    public void A_direct_push_with_no_pull_request_runs_everything()
    {
        var decision = Decide(Scenario(pulls: []));

        decision.Tested.Should().BeFalse();
        decision.Reason.Should().Contain("no merged PR");
    }

    [Fact]
    public void A_pull_request_that_never_merged_or_merged_elsewhere_runs_everything()
    {
        Decide(Scenario(pulls: [Pull(mergedAt: null)])).Tested.Should().BeFalse();
        Decide(Scenario(pulls: [Pull(baseRef: "release")])).Tested.Should().BeFalse();
    }

    [Fact]
    public void A_tree_that_differs_from_the_head_runs_everything()
    {
        // The squash of a branch that was behind main: main's other changes make a tree no PR run tested.
        var decision = Decide(Scenario(headTree: "tree-b"));

        decision.Tested.Should().BeFalse();
        decision.Reason.Should().Contain("differs");
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancelled")]
    [InlineData("timed_out")]
    [InlineData("action_required")]
    [InlineData(null)]
    public void A_check_that_did_not_pass_on_the_head_runs_everything(string? conclusion)
    {
        var checks = new[] { Check("Gate"), Check("Lighthouse (report-only)", conclusion: conclusion) };

        var decision = Decide(Scenario(checks: checks));

        decision.Tested.Should().BeFalse();
        decision.Reason.Should().Contain("Lighthouse (report-only)");
    }

    [Fact]
    public void A_check_still_running_on_the_head_runs_everything()
    {
        var checks = new[] { Check("Gate"), Check("Lighthouse (report-only)", status: "in_progress", conclusion: null) };

        var decision = Decide(Scenario(checks: checks));

        decision.Tested.Should().BeFalse();
        decision.Reason.Should().Contain("still running").And.Contain("Lighthouse (report-only)");
    }

    [Fact]
    public void A_head_without_a_passing_Gate_runs_everything()
    {
        Decide(Scenario(checks: [Check("Other")])).Reason.Should().Contain("no passing run on its head of: Gate");
        Decide(Scenario(checks: [Check("Gate", conclusion: "skipped")])).Tested.Should().BeFalse("a skipped Gate tested nothing");
        Decide(Scenario(checks: [])).Tested.Should().BeFalse();
    }

    [Fact]
    public void A_Gate_that_another_app_posted_passes_nothing()
    {
        Decide(Scenario(checks: [Check("Gate", app: "some-other-app")])).Tested.Should().BeFalse();
        Decide(Scenario(checks: [Check("Gate", app: null)])).Tested.Should().BeFalse();
    }

    [Theory]
    [InlineData("pulls")]
    [InlineData("tree")]
    [InlineData("checks")]
    public void An_error_from_the_API_runs_everything(string failingCall)
    {
        var decision = Decide(Scenario(fails: failingCall));

        decision.Tested.Should().BeFalse();
        decision.Reason.Should().Contain("500");
    }

    [Fact]
    public void A_second_pull_request_can_vouch_for_the_tree_when_the_first_cannot()
    {
        // GitHub lists every PR that contains the commit. The first one's head tree differs, the second's matches.
        var scenario = Scenario(pulls: [Pull(number: 5, head: "head-old"), Pull(number: 7)], extraTrees: new() { ["head-old"] = "tree-old" });

        var decision = Decide(scenario);

        decision.Tested.Should().BeTrue();
        decision.Reason.Should().Contain("PR #7");
    }

    [Fact]
    public void Every_page_of_the_heads_checks_is_read()
    {
        using var result = JsonDocument.Parse(Run(PagingDriver, "{}"));

        result.RootElement.GetProperty("count").GetInt32().Should().Be(150);
        result.RootElement.GetProperty("requested").EnumerateArray().Select(url => url.GetString()).Should().HaveCount(2)
            .And.AllSatisfy(url => url.Should().Contain("commits/head0/check-runs?filter=latest"));
    }

    [Fact]
    public void Run_without_a_token_it_runs_the_tests_and_writes_false_to_the_output_file()
    {
        var output = Path.GetTempFileName();
        try
        {
            var start = NodeStart([RepositoryFiles.PathTo("scripts", "tested-tree.mjs"), Pushed], useStandardInput: false);
            start.Environment["GITHUB_OUTPUT"] = output;
            start.Environment.Remove("GITHUB_TOKEN");
            start.Environment.Remove("GITHUB_REPOSITORY");

            var (exitCode, stdout, stderr) = Start(start);

            exitCode.Should().Be(0, stderr);
            stdout.Should().Contain("[tested-tree] running the tests").And.Contain("missing");
            File.ReadAllText(output).Trim().Should().Be("tested=false");
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static Decision Decide(string scenario)
    {
        using var result = JsonDocument.Parse(Run(Driver, scenario));
        return new Decision(result.RootElement.GetProperty("tested").GetBoolean(), result.RootElement.GetProperty("reason").GetString()!);
    }

    private static string Scenario(
        object[]? pulls = null, string headTree = "tree-a", object[]? checks = null, string? fails = null, Dictionary<string, string>? extraTrees = null)
    {
        var trees = new Dictionary<string, string> { [Pushed] = "tree-a", [Head] = headTree };
        foreach (var (sha, tree) in extraTrees ?? [])
        {
            trees[sha] = tree;
        }

        var greenChecks = checks ?? [Check("Gate")];
        var checksBySha = new Dictionary<string, object[]> { [Head] = greenChecks, ["head-old"] = greenChecks };
        return JsonSerializer.Serialize(new { pulls = pulls ?? [Pull()], trees, checks = checksBySha, fails });
    }

    private static object Pull(int number = 7, string? mergedAt = "2026-11-07T00:00:00Z", string baseRef = "main", string head = Head) =>
        new { number, merged_at = mergedAt, @base = new { @ref = baseRef }, head = new { sha = head } };

    private static object Check(string name, string status = "completed", string? conclusion = "success", string? app = "github-actions") =>
        new { name, status, conclusion, app = app is null ? null : new { slug = app } };

    /// <summary>Runs a driver script on node's standard input, with the scenario and the script's path in the environment.</summary>
    private static string Run(string driver, string scenario)
    {
        var start = NodeStart(["--input-type=module"], useStandardInput: true);
        start.Environment["TESTED_TREE_SCENARIO"] = scenario;
        var (exitCode, output, error) = Start(start, driver);
        exitCode.Should().Be(0, error);
        return output;
    }

    private static ProcessStartInfo NodeStart(string[] arguments, bool useStandardInput)
    {
        var start = new ProcessStartInfo("node", arguments)
        {
            RedirectStandardInput = useStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        start.Environment["TESTED_TREE_SCRIPT"] = RepositoryFiles.PathTo("scripts", "tested-tree.mjs");
        return start;
    }

    private static (int ExitCode, string Output, string Error) Start(ProcessStartInfo start, string? standardInput = null)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, errorTask.Result);
    }

    private sealed record Decision(bool Tested, string Reason);
}
