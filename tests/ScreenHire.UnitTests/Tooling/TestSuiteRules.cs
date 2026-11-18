using System.Text.RegularExpressions;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// What a scan can hold of <c>.claude/rules/test-value.md</c> and the flake policy (docs/process/testing.md), as checks
/// over file text, so the tests run them on the repository and on the same files edited in memory. The gate runs every
/// test: no changed-files selection, no retry, no skipped or exclusive test. And a test waits on a condition: no fixed
/// sleep outside the helpers that name what they wait for (<c>Wait</c> in <c>tests/ScreenHire.Testing</c>,
/// <c>nothingHappensFor</c> in the dashboard's <c>test-support</c>). <see cref="Problems"/> returns what breaks a
/// rule, empty when nothing does.
/// </summary>
internal static partial class TestSuiteRules
{
    /// <summary>
    /// Paths the checks leave out, each with its reason. A path ending in '/' is a folder.
    /// </summary>
    private static readonly (string Path, string Reason)[] Exempt =
    [
        ("tests/ScreenHire.Testing/", "the shared helpers hold the suite's only delays: the polling in Wait, Mailpit and the sandboxes, and a fake that answers late"),
        ("apps/dashboard/src/test-support/", "nothingHappensFor is the one timer a dashboard test may wait on"),
        ("tests/ScreenHire.UnitTests/Tooling/TestSuiteRules", "these checks and their tests name what they refuse"),
    ];

    private static readonly string[] SkippedFolders = ["node_modules", "bin", "obj", "dist", "build", ".svelte-kit", ".astro", "TestResults", ".vitest-cache"];

    private sealed record Rule(Regex Pattern, string Problem);

    // Where the gate is configured: the workflows, every package's scripts, the Vitest configurations and the .NET
    // test projects' packages.
    private static readonly Rule[] Configuration =
    [
        new(ChangedFilesSelection(), "selects tests by changed files. The gate runs every test: an import graph cannot see a test that reads source as text, and nothing follows a .NET change into the browser tests"),
        new(RetryInConfiguration(), "retries tests. A test that passes on a retry is a bug to fix at its cause (docs/process/testing.md, \"Flake policy\")"),
    ];

    private static readonly Rule[] VitestConfiguration =
    [
        new(VitestRetry(), "sets Vitest's retry. A test that passes on a retry is a bug to fix at its cause (docs/process/testing.md, \"Flake policy\")"),
    ];

    private static readonly Rule[] DotNetTests =
    [
        new(SkippedDotNetTest(), "skips a test, or runs it only when asked for. Fix it or remove it: a skipped test reads as covered and proves nothing"),
        new(DotNetSleep(), "sleeps for a fixed time. Wait on a condition with Wait.UntilAsync or Wait.ForAsync (tests/ScreenHire.Testing), or name the window with Wait.ForNothingAsync when the point is that nothing happens"),
    ];

    private static readonly Rule[] ScriptTests =
    [
        new(SkippedScriptTest(), "skips tests or runs only some. Fix the test or remove it: .only leaves every other test in the file unrun"),
        new(ScriptSleep(), "sleeps on a timer. Wait on a condition (waitFor, findBy...), or name the window with nothingHappensFor (test-support/quiet.ts) when the point is that nothing happens"),
    ];

    /// <summary>
    /// Every file the checks read, by its '/'-separated repo path: <c>.github/</c>, the package manifests and Vitest
    /// configurations, the .NET test projects and their sources, and the script tests.
    /// </summary>
    public static Dictionary<string, string> RepositoryRuleFiles()
    {
        var root = RepositoryFiles.PathTo();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        void Visit(string folder)
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var path = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (Rules(path).Length > 0)
                {
                    files[path] = File.ReadAllText(file).ReplaceLineEndings("\n");
                }
            }

            foreach (var child in Directory.EnumerateDirectories(folder).Where(child => !SkippedFolders.Contains(Path.GetFileName(child))))
            {
                Visit(child);
            }
        }

        foreach (var top in new[] { ".github", "apps", "packages", "tools", "tests" })
        {
            Visit(RepositoryFiles.PathTo(top));
        }

        foreach (var file in new[] { "package.json", "Directory.Packages.props" })
        {
            files[file] = File.ReadAllText(RepositoryFiles.PathTo(file)).ReplaceLineEndings("\n");
        }

        return files;
    }

    public static IReadOnlyList<string> Problems(IReadOnlyDictionary<string, string> files)
    {
        var problems = new List<string>();
        foreach (var (path, text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            if (Exempt.Any(exempt => path.StartsWith(exempt.Path, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var rule in Rules(path))
            {
                foreach (Match match in rule.Pattern.Matches(text))
                {
                    var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                    problems.Add($"{path}:{line}: `{match.Value.Trim()}` {rule.Problem}.");
                }
            }
        }

        return problems;
    }

    /// <summary>The checks a path gets, by what kind of file it is. None means the checks do not read it.</summary>
    private static Rule[] Rules(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (path.StartsWith(".github/", StringComparison.Ordinal) || name is "package.json" or "Directory.Packages.props" || name.EndsWith(".csproj", StringComparison.Ordinal))
        {
            return Configuration;
        }

        if (VitestConfigurationName().IsMatch(name))
        {
            return [.. Configuration, .. VitestConfiguration];
        }

        if (path.StartsWith("tests/", StringComparison.Ordinal) && name.EndsWith(".cs", StringComparison.Ordinal))
        {
            return DotNetTests;
        }

        return ScriptTestName().IsMatch(name) || path.Contains("/test-support/", StringComparison.Ordinal) ? ScriptTests : [];
    }

    [GeneratedRegex(@"^(?:vitest|vite)\.config\.[cm]?[jt]s$")]
    private static partial Regex VitestConfigurationName();

    [GeneratedRegex(@"\.(?:test|spec)\.[cm]?[jt]sx?$")]
    private static partial Regex ScriptTestName();

    // Vitest's --changed and `vitest related`, Playwright's --only-changed.
    [GeneratedRegex(@"--changed\b|--only-changed\b|\bvitest\s+related\b")]
    private static partial Regex ChangedFilesSelection();

    // Microsoft Testing Platform's retry extension and its option, and the xRetry package.
    [GeneratedRegex(@"--retry-failed-tests\b|Microsoft\.Testing\.Extensions\.Retry|\bxRetry\b")]
    private static partial Regex RetryInConfiguration();

    [GeneratedRegex(@"\bretry\s*:")]
    private static partial Regex VitestRetry();

    // Skip on a test attribute, a skip decided while the test runs, and a test that runs only when named.
    [GeneratedRegex(@"\[(?:Fact|Theory)\s*\([^\]\n]*\b(?:Skip|Explicit)\s*=[^\]\n]*\)\]|\bAssert\.Skip(?:When|Unless)?\s*\(")]
    private static partial Regex SkippedDotNetTest();

    [GeneratedRegex(@"\bTask\.Delay\s*\(|\bThread\.Sleep\s*\(|\bWaitForTimeoutAsync\s*\(")]
    private static partial Regex DotNetSleep();

    [GeneratedRegex(@"\b(?:it|test|describe)\.(?:skip|only|todo|skipIf|runIf|fails)\b")]
    private static partial Regex SkippedScriptTest();

    // A promise that resolves on a timer of more than zero: `setTimeout(resolve, 50)`. A zero or absent delay is a turn
    // of the event loop, not a wait.
    [GeneratedRegex(@"\bsetTimeout\(\s*\w+\s*,\s*[1-9]\d*\s*\)")]
    private static partial Regex ScriptSleep();
}
