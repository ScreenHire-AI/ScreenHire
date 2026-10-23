using System.Diagnostics;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// docs/ is one Open Knowledge Format v0.2 bundle (ADR-0012), so an agent can walk from docs/index.md to the one concept
/// that answers its question by reading descriptions. Every check in <see cref="DocsBundle"/> runs twice here: against
/// the repository, and against a small fixture bundle that passes everything until one defect breaks exactly the rule
/// that check enforces. The fixture keeps every check honest: a check that cannot fail would pass the repository too.
/// </summary>
public sealed class DocsBundleTests
{
    private static readonly Lazy<DocsBundle> Repository = new(() => DocsBundle.Load(RepositoryFiles.PathTo()));

    public static TheoryData<string> CheckNames => [.. DocsBundle.Checks.Keys];

    /// <summary>One defect per rule: the check it must trip, and the edit that breaks the valid fixture.</summary>
    public static TheoryData<string, string> Defects => new()
    {
        { "concept frontmatter", "a concept without frontmatter" },
        { "concept frontmatter", "frontmatter the parser cannot read" },
        { "concept fields", "a type outside the list" },
        { "concept fields", "a source that does not exist" },
        { "concept fields", "a folder as a source" },
        { "concept fields", "no description" },
        { "title is the H1", "an H1 that is not the title" },
        { "unique descriptions", "two concepts with one description" },
        { "an index per folder", "a folder with no index" },
        { "okf_version on the root only", "frontmatter on a folder index" },
        { "okf_version on the root only", "no okf_version on the root index" },
        { "index entries", "an index entry that differs from the description" },
        { "index entries", "a concept missing from its index" },
        { "subfolder indexes linked", "a subfolder index the parent does not link" },
        { "links resolve", "a link to a missing file" },
        { "links resolve", "a link to a missing heading" },
        { "no instruction file names", "a doc named AGENTS.md" },
        { "log newest first", "log entries oldest first" },
        { "log newest first", "a log entry that is not a date" },
        { "docs citations exist", "a citation of a missing doc" },
        { "docs citations exist", "a citation of a missing heading" },
    };

    [Fact]
    public void The_repository_bundle_is_not_vacuous()
    {
        var bundle = Repository.Value;

        bundle.Markdown.Should().HaveCountGreaterThan(30);
        bundle.Markdown.Select(file => file.Path).Should().Contain(["docs/index.md", "docs/log.md", "docs/adr/index.md"]);
        bundle.Markdown.SelectMany(file => file.Links).Count(link => link.Fragment is not null)
            .Should().BeGreaterThan(5, "the fragment check reads real fragment links");
        var citations = bundle.Citations().ToList();
        citations.Should().HaveCountGreaterThan(100, "the citation scan reads code, skills and rules");
        citations.Select(citation => citation.File).Should().Contain(["CLAUDE.md", "apps/apphost/AppHost.cs", ".claude/agents/design-review.md"]);
    }

    [Theory]
    [MemberData(nameof(CheckNames))]
    public void The_repository_passes(string check)
    {
        DocsBundle.Checks[check](Repository.Value).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(CheckNames))]
    public void The_valid_fixture_passes(string check)
    {
        using var fixture = new Fixture();

        DocsBundle.Checks[check](DocsBundle.Load(fixture.Root)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Defects))]
    public void A_defect_fails_its_check(string check, string defect)
    {
        using var fixture = new Fixture();
        fixture.Break(defect);

        DocsBundle.Checks[check](DocsBundle.Load(fixture.Root)).Should().NotBeEmpty($"the fixture has {defect}");
    }

    [Fact]
    public void Sources_names_the_concepts_that_list_a_file()
    {
        var start = new ProcessStartInfo("node", [RepositoryFiles.PathTo("scripts", "docs-bundle.mjs"), "sources", "src/ScreenHire.Application/Automations/Webhooks.cs"])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0);
        output.Should().Contain("docs/architecture/workflow-engine.md");
    }

    /// <summary>A two-folder bundle in a temporary git repository that passes every check.</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"screenhire-docs-{Guid.NewGuid():N}");
            Write("CLAUDE.md", "Start at docs/index.md; setup is docs/guides/setup.md#install.\n");
            Write("docs/index.md", """
                ---
                okf_version: "0.2"
                ---
                # Fixture docs

                * [Start here](start.md) - Where to start reading the fixture.
                * [Guides](guides/index.md) - The guides.
                * [Log](log.md) - Dated changes.
                """);
            Write("docs/start.md", """
                ---
                type: Guide
                title: Start here
                description: Where to start reading the fixture.
                tags: [start]
                sources:
                  - resource: CLAUDE.md
                ---
                # Start here

                Install with [setup](guides/setup.md#install), then read [the log](log.md).
                """);
            Write("docs/log.md", """
                # Log

                ## 2026-10-23

                The newest entry.

                ## 2026-10-01

                The oldest entry.
                """);
            Write("docs/guides/index.md", """
                # Guides

                * [Setup](setup.md) - Installing the fixture and its one command.
                """);
            Write("docs/guides/setup.md", """
                ---
                type: Guide
                title: Setup
                description: Installing the fixture and its one command.
                ---
                # Setup

                ## Install

                Back to [the start](../start.md#start-here).
                """);
            Git("init", "-q");
        }

        public string Root { get; }

        public void Break(string defect)
        {
            switch (defect)
            {
                case "a concept without frontmatter":
                    Write("docs/guides/setup.md", "# Setup\n\n## Install\n");
                    break;
                case "frontmatter the parser cannot read":
                    Replace("docs/guides/setup.md", "type: Guide", "type Guide");
                    break;
                case "a type outside the list":
                    Replace("docs/guides/setup.md", "type: Guide", "type: Tutorial");
                    break;
                case "a source that does not exist":
                    Replace("docs/start.md", "resource: CLAUDE.md", "resource: src/Missing.cs");
                    break;
                case "a folder as a source":
                    Replace("docs/start.md", "resource: CLAUDE.md", "resource: docs/guides/");
                    break;
                case "no description":
                    Replace("docs/guides/setup.md", "description: Installing the fixture and its one command.\n", "");
                    break;
                case "an H1 that is not the title":
                    Replace("docs/guides/setup.md", "# Setup", "# Set up");
                    break;
                case "two concepts with one description":
                    Replace("docs/guides/setup.md", "description: Installing the fixture and its one command.", "description: Where to start reading the fixture.");
                    break;
                case "a folder with no index":
                    File.Delete(PathOf("docs/guides/index.md"));
                    break;
                case "frontmatter on a folder index":
                    Replace("docs/guides/index.md", "# Guides", "---\nokf_version: \"0.2\"\n---\n# Guides");
                    break;
                case "no okf_version on the root index":
                    Replace("docs/index.md", "okf_version: \"0.2\"", "okf_version: \"0.1\"");
                    break;
                case "an index entry that differs from the description":
                    Replace("docs/guides/index.md", "its one command.", "its commands.");
                    break;
                case "a concept missing from its index":
                    Write("docs/guides/upgrade.md", "---\ntype: Guide\ntitle: Upgrade\ndescription: Upgrading the fixture.\n---\n# Upgrade\n");
                    break;
                case "a subfolder index the parent does not link":
                    Replace("docs/index.md", "* [Guides](guides/index.md) - The guides.\n", "");
                    break;
                case "a link to a missing file":
                    Replace("docs/start.md", "(log.md)", "(changes.md)");
                    break;
                case "a link to a missing heading":
                    Replace("docs/start.md", "setup.md#install", "setup.md#installing");
                    break;
                case "a doc named AGENTS.md":
                    Write("docs/guides/AGENTS.md", "Agents: read the guides.\n");
                    break;
                case "log entries oldest first":
                    Replace("docs/log.md", "## 2026-10-01", "## 2026-10-30");
                    break;
                case "a log entry that is not a date":
                    Replace("docs/log.md", "## 2026-10-01", "## October");
                    break;
                case "a citation of a missing doc":
                    Replace("CLAUDE.md", "docs/index.md", "docs/README.md");
                    break;
                case "a citation of a missing heading":
                    Replace("CLAUDE.md", "setup.md#install", "setup.md#uninstall");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(defect), defect, "No such defect.");
            }
        }

        public void Dispose()
        {
            // git marks its object files read-only, which Directory.Delete refuses on Windows.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }

        private string PathOf(string repoPath) => Path.Combine(Root, repoPath);

        private void Write(string repoPath, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(repoPath))!);
            File.WriteAllText(PathOf(repoPath), text.ReplaceLineEndings("\n") + (text.EndsWith('\n') ? "" : "\n"));
        }

        private void Replace(string repoPath, string from, string to)
        {
            var text = File.ReadAllText(PathOf(repoPath));
            text.Should().Contain(from, $"the defect edits {repoPath}");
            File.WriteAllText(PathOf(repoPath), text.Replace(from, to, StringComparison.Ordinal));
        }

        private void Git(params string[] arguments)
        {
            using var process = Process.Start(new ProcessStartInfo("git", ["-C", Root, .. arguments]) { UseShellExecute = false })
                ?? throw new InvalidOperationException("Could not start git.");
            process.WaitForExit();
            process.ExitCode.Should().Be(0);
        }
    }
}
