using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The upgrade researcher's hook (<c>scripts/upgrade-researcher-guard.mjs</c>), run the way Claude Code runs a PreToolUse
/// hook: the tool call as JSON on stdin, a deny decision as JSON on stdout, nothing when the call may run. The researcher
/// reads release notes and packages that can be minutes old, so it may read a package and never run one. The allowed rows
/// matter as much as the refused ones: a guard that refuses its own reads leaves research with nothing to go on.
/// </summary>
public sealed class UpgradeResearcherGuardTests
{
    public static TheoryData<string> Reads => new()
    {
        "npm view astro@7.4.12 peerDependencies engines --json",
        "npm view @sveltejs/kit repository.url",
        "dotnet package search Aspire.Hosting.PostgreSQL --exact-match --format json",
        "gh api --paginate repos/withastro/astro/releases",
        "gh api -H \"Accept: application/vnd.github.raw\" repos/dotnet/aspire/contents/README.md",
        "gh api \"repos/owner/repo/compare/v1.0.0...v1.1.0?per_page=100&page=2\"",
        "gh search issues regression 17.0.0 --repo dotnet/aspire",
        "gh release view v6.64.2 --repo JasperFx/wolverine",
        "gh issue list --repo owner/repo --search \"a && b\"",
        "git log -S'\"astro\": \"^7.4.0\"' -- apps/careers/package.json",
        "git grep -n WolverineFx",
        "ls apps",
        "node -p \"new Date().toISOString()\"",
    };

    public static TheoryData<string> Runs => new()
    {
        "node -e \"require('astro')\"",
        "npm pack astro@7.4.12",
        "npm install astro@7.4.12",
        "pnpm add astro@7.4.12",
        "dotnet package add Aspire.Hosting",
        "dotnet build",
        "curl -sL https://registry.npmjs.org/astro",
        "tar -xzf astro.tgz",
        "gh api -X POST repos/owner/repo/issues",
        "gh api --method PATCH repos/owner/repo",
        "gh api repos/owner/repo/issues -f title=x",
        "git log --output=notes.txt",
        "git grep -Ocat pattern",
        "dotnet package search Aspire.Hosting --interactive",
        "npm view astro && rm -rf apps",
        "npm view astro | tee out.txt",
        "gh api repos/owner/repo/releases > releases.json",
        "npm view $(cat name.txt)",
        "npm view `cat name.txt`",
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Read_only_commands_run(string command)
    {
        (await DenyReasonAsync("Bash", new JsonObject { ["command"] = command })).Should().BeNull($"`{command}` only reads");
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public async Task Anything_that_runs_installs_writes_or_chains_is_refused(string command)
    {
        (await DenyReasonAsync("Bash", new JsonObject { ["command"] = command })).Should().NotBeNull($"`{command}` is not a single read");
    }

    [Fact]
    public async Task A_Bash_call_with_no_command_is_refused()
    {
        (await DenyReasonAsync("Bash", new JsonObject())).Should().NotBeNull();
    }

    [Theory]
    [InlineData(@"C:\Users\dev\ScreenHire\.upgrade-deps\briefs\astro.json")]
    [InlineData("/home/dev/ScreenHire/.upgrade-deps/briefs/aspire-major.json")]
    public async Task A_brief_may_be_written(string path)
    {
        (await DenyReasonAsync("Write", new JsonObject { ["file_path"] = path })).Should().BeNull();
    }

    [Theory]
    [InlineData("Write", @"C:\Users\dev\ScreenHire\scripts\upgrade-deps.mjs")]
    [InlineData("Write", "/home/dev/ScreenHire/.upgrade-deps/briefs/../../package.json")]
    [InlineData("Write", "/home/dev/ScreenHire/.upgrade-deps/briefs/notes.txt")]
    [InlineData("Edit", "/home/dev/ScreenHire/Directory.Packages.props")]
    public async Task Nothing_but_a_brief_may_be_written(string tool, string path)
    {
        (await DenyReasonAsync(tool, new JsonObject { ["file_path"] = path })).Should().Contain("briefs");
    }

    [Fact]
    public async Task PowerShell_is_refused_outright()
    {
        (await DenyReasonAsync("PowerShell", new JsonObject { ["command"] = "Get-ChildItem" })).Should().NotBeNull();
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("WebFetch")]
    [InlineData("Grep")]
    public async Task Other_tools_pass(string tool)
    {
        (await DenyReasonAsync(tool, new JsonObject { ["file_path"] = "/home/dev/ScreenHire/package.json" })).Should().BeNull();
    }

    [Fact]
    public async Task Input_the_guard_cannot_read_is_refused()
    {
        (await RunAsync("not json")).Should().Contain("could not read");
    }

    [Fact]
    public void The_agent_declares_the_hook_so_removing_it_fails_here()
    {
        var agent = File.ReadAllText(RepositoryFiles.PathTo(".claude", "agents", "upgrade-researcher.md"));
        var frontmatter = agent.Split("---", 3, StringSplitOptions.RemoveEmptyEntries)[0];

        frontmatter.Should().Contain("PreToolUse").And.Contain("scripts/upgrade-researcher-guard.mjs");
        frontmatter.Should().MatchRegex(@"matcher: ""[^""]*\bBash\b[^""]*\bWrite\b[^""]*""");
    }

    private static Task<string?> DenyReasonAsync(string tool, JsonObject toolInput) =>
        RunAsync(new JsonObject { ["tool_name"] = tool, ["tool_input"] = toolInput }.ToJsonString());

    private static async Task<string?> RunAsync(string standardInput)
    {
        var start = new ProcessStartInfo("node", [RepositoryFiles.PathTo("scripts", "upgrade-researcher-guard.mjs")])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        await process.StandardInput.WriteAsync(standardInput);
        process.StandardInput.Close();

        var cancellationToken = TestContext.Current.CancellationToken;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        process.ExitCode.Should().Be(0, error);

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var decision = JsonNode.Parse(output)!["hookSpecificOutput"]!;
        decision["permissionDecision"]!.GetValue<string>().Should().Be("deny");
        return decision["permissionDecisionReason"]!.GetValue<string>();
    }
}
