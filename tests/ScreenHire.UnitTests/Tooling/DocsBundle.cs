using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The docs/ bundle as <c>scripts/docs-bundle.mjs model</c> reads it, and the checks that hold it to the Open Knowledge
/// Format v0.2 layout (ADR-0012). The script owns parsing (frontmatter, GitHub's heading anchors, links), the same
/// helpers <c>/sync-docs</c> runs, so the tests and the skill cannot disagree; this class only judges the result.
/// Each check returns its problems, empty when the bundle passes.
/// </summary>
internal sealed partial class DocsBundle
{
    /// <summary>The concept types, as <c>CONCEPT_TYPES</c> in the script lists them.</summary>
    private static readonly string[] ConceptTypes = ["Guide", "Reference", "Design", "Decision", "Audit", "Research"];

    /// <summary>
    /// File names an agent CLI loads as instructions when it reads a folder, compared case-insensitively. Claude Code
    /// reads a claude.md under docs as a CLAUDE.md on a case-insensitive file system and puts the whole doc into context.
    /// </summary>
    private static readonly string[] InstructionFileNames =
        ["claude.md", "claude.local.md", "agents.md", "gemini.md", "qwen.md", "warp.md", "grok.md"];

    /// <summary>
    /// Files whose <c>docs/</c> paths are not citations: the docs log (history names what a path was), and this check
    /// and its tests, whose examples and fixtures name made-up docs on purpose.
    /// </summary>
    private static readonly string[] CitationExempt =
        ["docs/log.md", "tests/ScreenHire.UnitTests/Tooling/DocsBundle.cs", "tests/ScreenHire.UnitTests/Tooling/DocsBundleTests.cs"];

    private DocsBundle(string root, Model model)
    {
        Root = root;
        Files = model.Files;
        Markdown = model.Markdown;
        OutsideAnchors = model.OutsideAnchors;
    }

    public string Root { get; }

    public IReadOnlyList<string> Files { get; }

    public IReadOnlyList<MarkdownFile> Markdown { get; }

    public IReadOnlyDictionary<string, string[]> OutsideAnchors { get; }

    /// <summary>The checks by name, so a test can run one at a time against the repository and against a fixture.</summary>
    public static IReadOnlyDictionary<string, Func<DocsBundle, IReadOnlyList<string>>> Checks { get; } =
        new Dictionary<string, Func<DocsBundle, IReadOnlyList<string>>>
        {
            ["concept frontmatter"] = bundle => bundle.ConceptsWithoutFrontmatter(),
            ["concept fields"] = bundle => bundle.ConceptFieldProblems(),
            ["title is the H1"] = bundle => bundle.TitleProblems(),
            ["unique descriptions"] = bundle => bundle.SharedDescriptions(),
            ["an index per folder"] = bundle => bundle.FoldersWithoutIndex(),
            ["okf_version on the root only"] = bundle => bundle.OkfVersionProblems(),
            ["index entries"] = bundle => bundle.IndexEntryProblems(),
            ["subfolder indexes linked"] = bundle => bundle.UnlinkedSubfolderIndexes(),
            ["links resolve"] = bundle => bundle.BrokenLinks(),
            ["no instruction file names"] = bundle => bundle.InstructionNamedFiles(),
            ["log newest first"] = bundle => bundle.LogProblems(),
            ["docs citations exist"] = bundle => bundle.MissingCitations(),
        };

    /// <summary>Reads the bundle under <paramref name="root"/> through the script.</summary>
    public static DocsBundle Load(string root)
    {
        var start = new ProcessStartInfo("node", [RepositoryFiles.PathTo("scripts", "docs-bundle.mjs"), "model", "--root", root])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // A redirected stream defaults to the console code page on Windows, which garbles non-ASCII titles.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docs-bundle.mjs model failed: {errorTask.Result}");
        }

        var model = JsonSerializer.Deserialize<Model>(output, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("docs-bundle.mjs printed no model.");
        return new DocsBundle(root, model);
    }

    private IEnumerable<MarkdownFile> Concepts => Markdown.Where(file => IsConcept(file.Path));

    private IEnumerable<string> IndexFolders =>
        Markdown.Where(file => FileName(file.Path) == "index.md").Select(file => Folder(file.Path));

    private IReadOnlyList<string> ConceptsWithoutFrontmatter() =>
        Concepts.Where(file => file.Frontmatter is null)
            .Select(file => file.FrontmatterError is null
                ? $"{file.Path}: no frontmatter (every docs markdown file but index.md and log.md is a concept)"
                : $"{file.Path}: {file.FrontmatterError}")
            .ToList();

    private IReadOnlyList<string> ConceptFieldProblems()
    {
        var problems = new List<string>();
        foreach (var file in Concepts.Where(file => file.Frontmatter is not null))
        {
            var type = Text(file, "type");
            if (!ConceptTypes.Contains(type))
            {
                problems.Add($"{file.Path}: type \"{type}\" is not one of {string.Join(", ", ConceptTypes)}");
            }

            if (string.IsNullOrWhiteSpace(Text(file, "title")))
            {
                problems.Add($"{file.Path}: no title");
            }

            var description = Text(file, "description");
            if (string.IsNullOrWhiteSpace(description) || description.Contains('\n'))
            {
                problems.Add($"{file.Path}: the description must be one non-empty line");
            }

            if (file.Frontmatter!["tags"] is { } tags and not JsonArray)
            {
                problems.Add($"{file.Path}: tags must be a list, not {tags.ToJsonString()}");
            }

            if (file.Frontmatter["sources"] is { } sources and not JsonArray)
            {
                problems.Add($"{file.Path}: sources must be a list of `- resource: <path>` entries");
            }

            foreach (var source in (file.Frontmatter["sources"] as JsonArray ?? []).Select(SourcePath))
            {
                if (string.IsNullOrEmpty(source) || !PathExists(source))
                {
                    problems.Add($"{file.Path}: source \"{source}\" does not exist");
                }
            }
        }

        return problems;
    }

    private IReadOnlyList<string> TitleProblems()
    {
        var problems = new List<string>();
        foreach (var file in Concepts.Where(file => file.Frontmatter is not null))
        {
            var title = Text(file, "title");
            var firstHeading = file.Headings.FirstOrDefault();
            if (firstHeading is not { Level: 1 } || firstHeading.Text != title)
            {
                problems.Add($"{file.Path}: the first heading must be \"# {title}\", the concept's title");
            }
        }

        return problems;
    }

    private IReadOnlyList<string> SharedDescriptions() =>
        Concepts.Where(file => !string.IsNullOrWhiteSpace(Text(file, "description")))
            .GroupBy(file => Text(file, "description"))
            .Where(group => group.Count() > 1)
            .Select(group => $"{string.Join(" and ", group.Select(file => file.Path))} share a description; say what only each one holds")
            .ToList();

    /// <summary>
    /// Every folder holding markdown, directly or below, docs/ included. A folder holding only subfolders counts too:
    /// without an index of its own nothing links its subfolders' indexes, and that branch is unreachable.
    /// </summary>
    private IReadOnlyList<string> FoldersWithoutIndex()
    {
        var folders = new HashSet<string>();
        foreach (var file in Markdown)
        {
            for (var folder = Folder(file.Path); folder.StartsWith("docs", StringComparison.Ordinal); folder = Folder(folder))
            {
                folders.Add(folder);
            }
        }

        var indexed = IndexFolders.ToHashSet();
        return folders.Where(folder => !indexed.Contains(folder)).Order().Select(folder => $"{folder}: no index.md").ToList();
    }

    private IReadOnlyList<string> OkfVersionProblems()
    {
        var problems = new List<string>();
        var root = Markdown.FirstOrDefault(file => file.Path == "docs/index.md");
        if (root?.Frontmatter is not { Count: 1 } frontmatter || frontmatter["okf_version"]?.GetValue<string>() != "0.2")
        {
            problems.Add("docs/index.md: the frontmatter must be exactly okf_version: \"0.2\"");
        }

        problems.AddRange(Markdown
            .Where(file => file.Path != "docs/index.md" && !IsConcept(file.Path) && file.Frontmatter is not null)
            .Select(file => $"{file.Path}: only docs/index.md carries frontmatter among the index and log files"));
        return problems;
    }

    /// <summary>Each index lists each sibling concept as <c>* [Title](file.md) - description</c>, with its exact description.</summary>
    private IReadOnlyList<string> IndexEntryProblems()
    {
        var problems = new List<string>();
        foreach (var folder in IndexFolders)
        {
            var indexPath = $"{folder}/index.md";
            var entries = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(Path.Combine(Root, indexPath)))
            {
                var match = IndexEntry().Match(line);
                if (match.Success)
                {
                    entries[match.Groups["file"].Value] = match.Groups["description"].Value.Trim();
                }
            }

            foreach (var concept in Concepts.Where(file => Folder(file.Path) == folder))
            {
                var name = FileName(concept.Path);
                var description = Text(concept, "description");
                if (!entries.TryGetValue(name, out var listed))
                {
                    problems.Add($"{indexPath}: no \"* [Title]({name}) - description\" entry");
                }
                else if (listed != description)
                {
                    problems.Add($"{indexPath}: the entry for {name} must read \"{description}\", its description");
                }
            }
        }

        return problems;
    }

    private IReadOnlyList<string> UnlinkedSubfolderIndexes()
    {
        var problems = new List<string>();
        foreach (var folder in IndexFolders.Where(folder => folder != "docs"))
        {
            var parentIndex = $"{Folder(folder)}/index.md";
            var parent = Markdown.FirstOrDefault(file => file.Path == parentIndex);
            var target = $"{FileName(folder)}/index.md";
            if (parent is null || !parent.Links.Any(link => link.Path == target))
            {
                problems.Add($"{parentIndex} does not link {target}");
            }
        }

        return problems;
    }

    private IReadOnlyList<string> BrokenLinks()
    {
        var anchors = Markdown.ToDictionary(file => file.Path, file => file.Headings.Select(heading => heading.Slug).ToHashSet());
        foreach (var (outside, slugs) in OutsideAnchors)
        {
            anchors[outside] = [.. slugs];
        }

        var problems = new List<string>();
        foreach (var file in Markdown)
        {
            foreach (var link in file.Links)
            {
                var target = link.Path.Length == 0 ? file.Path : Normalize($"{Folder(file.Path)}/{link.Path}");
                if (!PathExists(target))
                {
                    problems.Add($"{file.Path}:{link.Line} links to a missing file: {link.Url}");
                }
                else if (link.Fragment is { Length: > 0 } fragment && anchors.TryGetValue(target, out var slugs) && !slugs.Contains(fragment))
                {
                    problems.Add($"{file.Path}:{link.Line} links to a missing heading: {link.Url}");
                }
            }
        }

        return problems;
    }

    private IReadOnlyList<string> InstructionNamedFiles() =>
        Files.Where(file => InstructionFileNames.Contains(FileName(file), StringComparer.OrdinalIgnoreCase))
            .Select(file => $"{file}: an agent CLI loads this name as instructions; name the file after its subject")
            .ToList();

    private IReadOnlyList<string> LogProblems()
    {
        var log = Markdown.FirstOrDefault(file => file.Path == "docs/log.md");
        if (log is null)
        {
            return ["docs/log.md: missing"];
        }

        var dates = log.Headings.Where(heading => heading.Level == 2).Select(heading => heading.Text).ToList();
        var problems = dates.Where(date => !LogDate().IsMatch(date))
            .Select(date => $"docs/log.md: \"## {date}\" is not a ## YYYY-MM-DD entry")
            .ToList();
        if (dates.Count == 0)
        {
            problems.Add("docs/log.md: no ## YYYY-MM-DD entries");
        }
        else if (!dates.SequenceEqual(dates.OrderDescending(StringComparer.Ordinal)))
        {
            problems.Add($"docs/log.md: entries must run newest first, not {string.Join(", ", dates)}");
        }

        return problems;
    }

    /// <summary>
    /// Every <c>docs/...md</c> path cited in the root markdown, <c>.claude/</c>, the docs and the code (comments, skills,
    /// rules, agents) exists, and a <c>#fragment</c> after it names a heading. A moved doc otherwise leaves each citation
    /// pointing at nothing, and no link checker reads a backticked path or a source comment.
    /// </summary>
    private IReadOnlyList<string> MissingCitations()
    {
        var anchors = Markdown.ToDictionary(file => file.Path, file => file.Headings.Select(heading => heading.Slug).ToHashSet());
        var problems = new List<string>();
        foreach (var (file, cited, fragment) in Citations())
        {
            if (!PathExists(cited))
            {
                problems.Add($"{file} cites {cited}, which does not exist");
            }
            else if (fragment.Length > 0 && anchors.TryGetValue(cited, out var slugs) && !slugs.Contains(fragment))
            {
                problems.Add($"{file} cites {cited}#{fragment}, which is not a heading there");
            }
        }

        return problems;
    }

    /// <summary>Every docs path cited outside a URL, with the citing file and the fragment (empty when there is none).</summary>
    public IEnumerable<(string File, string Path, string Fragment)> Citations()
    {
        foreach (var file in TrackedFiles().Where(IsCitationSource))
        {
            var text = File.ReadAllText(Path.Combine(Root, file));
            if (!text.Contains("docs/", StringComparison.Ordinal) || text.Contains('\0'))
            {
                continue;
            }

            // A URL's docs/ path belongs to that site, not to this repository.
            foreach (Match match in DocsCitation().Matches(Url().Replace(text, " ")))
            {
                yield return (file, Normalize(match.Groups["path"].Value), match.Groups["fragment"].Value);
            }
        }
    }

    private static bool IsCitationSource(string file) =>
        !CitationExempt.Contains(file)
        && (file is "CLAUDE.md" or "README.md"
            || file.StartsWith(".claude/", StringComparison.Ordinal)
            || file.StartsWith("docs/", StringComparison.Ordinal)
            || file.StartsWith("apps/", StringComparison.Ordinal)
            || file.StartsWith("src/", StringComparison.Ordinal)
            || file.StartsWith("packages/", StringComparison.Ordinal)
            || file.StartsWith("scripts/", StringComparison.Ordinal)
            || file.StartsWith("tests/", StringComparison.Ordinal));

    /// <summary>Tracked and untracked files git does not ignore, so build output and node_modules stay out.</summary>
    private IEnumerable<string> TrackedFiles()
    {
        var start = new ProcessStartInfo("git", ["-C", Root, "ls-files", "-co", "--exclude-standard"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed: {errorTask.Result}");
        }

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(file => File.Exists(Path.Combine(Root, file)));
    }

    private bool PathExists(string repoPath)
    {
        var full = Path.Combine(Root, repoPath);
        return File.Exists(full) || Directory.Exists(full);
    }

    private static bool IsConcept(string docPath) => FileName(docPath) is not ("index.md" or "log.md");

    private static string Text(MarkdownFile file, string key) =>
        file.Frontmatter?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static string SourcePath(JsonNode? entry) => entry switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonObject item when item["resource"] is JsonValue resource && resource.TryGetValue<string>(out var text) => text,
        _ => "",
    };

    private static string Folder(string repoPath) =>
        repoPath.LastIndexOf('/') is var slash and > 0 ? repoPath[..slash] : "";

    private static string FileName(string repoPath) => repoPath[(repoPath.LastIndexOf('/') + 1)..];

    /// <summary>A '/'-separated path with <c>.</c> and <c>..</c> segments resolved, relative to the repository root.</summary>
    private static string Normalize(string repoPath)
    {
        var segments = new List<string>();
        foreach (var segment in repoPath.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    [GeneratedRegex(@"^\* \[[^\]]+\]\((?<file>[^)#]+)\) - (?<description>.+)$")]
    private static partial Regex IndexEntry();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex LogDate();

    /// <summary>
    /// A docs path as a bare repo path (<c>docs/x.md</c>) or a relative one (<c>../../docs/x.md</c>, read as the
    /// repository's docs), with an optional fragment. The path must start the token, so <c>src/content/docs/x.md</c> and
    /// <c>sync-docs/SKILL.md</c> are not citations.
    /// </summary>
    [GeneratedRegex(@"(?<![\w./-])(?:\.\.?/)*(?<path>docs/[\w./-]*?\.md)(?![\w])(?:#(?<fragment>[\w-]+))?")]
    private static partial Regex DocsCitation();

    [GeneratedRegex(@"https?://[^\s)\]'""`>]+")]
    private static partial Regex Url();

    public sealed record MarkdownFile(string Path, JsonObject? Frontmatter, string? FrontmatterError, Heading[] Headings, Link[] Links);

    public sealed record Heading(int Level, string Text, int Line, string Slug);

    public sealed record Link(string Url, string Path, string? Fragment, int Line);

    private sealed record Model(string[] Files, MarkdownFile[] Markdown, Dictionary<string, string[]> OutsideAnchors);
}
