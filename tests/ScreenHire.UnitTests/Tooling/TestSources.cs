using System.Text.RegularExpressions;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// What the test projects' sources say about their classes, read as text so the tests can run the same checks on the
/// repository and on the same files edited in memory: which classes hold tests, which run alone (a collection with
/// <c>DisableParallelization</c>) and which boot an AppHost of their own (a class fixture that derives from
/// <c>AppHostFixture</c>). The unit tests cannot list another test project's tests, since it may not be built.
/// </summary>
internal static partial class TestSources
{
    /// <summary>The projects CI runs in shards, by the root namespace <c>scripts/test-shard.mjs</c> knows them by.</summary>
    public static readonly string[] ShardedProjects = ["ScreenHire.E2E", "ScreenHire.IntegrationTests"];

    public sealed record TestClass(string FullName, string? Collection, IReadOnlyList<string> ClassFixtures);

    /// <summary>Every C# file under <c>tests/</c>, by its '/'-separated repo path. Build output is left out.</summary>
    public static Dictionary<string, string> RepositoryTestFiles()
    {
        var root = RepositoryFiles.PathTo();
        return Directory.EnumerateFiles(RepositoryFiles.PathTo("tests"), "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(root, path).Replace('\\', '/'), File: path))
            .Where(file => !file.Path.Contains("/bin/", StringComparison.Ordinal) && !file.Path.Contains("/obj/", StringComparison.Ordinal))
            .ToDictionary(file => file.Path, file => File.ReadAllText(file.File).ReplaceLineEndings("\n"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Every class with a <c>[Fact]</c> or a <c>[Theory]</c>, by its full name. An abstract class is a base for others
    /// and runs nothing itself.
    /// </summary>
    public static IReadOnlyList<TestClass> TestClasses(IReadOnlyDictionary<string, string> files)
    {
        var classes = new List<TestClass>();
        foreach (var text in files.Values)
        {
            var space = FileNamespace().Match(text) is { Success: true } found ? found.Groups["name"].Value : null;
            var declarations = ClassDeclaration().Matches(text);
            for (var index = 0; index < declarations.Count; index++)
            {
                var declaration = declarations[index];
                var body = text[(declaration.Index + declaration.Length)..(index + 1 < declarations.Count ? declarations[index + 1].Index : text.Length)];
                if (space is null || declaration.Groups["modifiers"].Value.Contains("abstract", StringComparison.Ordinal) || !TestAttribute().IsMatch(body))
                {
                    continue;
                }

                classes.Add(new TestClass(
                    $"{space}.{declaration.Groups["name"].Value}",
                    CollectionAttribute().Match(declaration.Groups["attributes"].Value) is { Success: true } collection ? collection.Groups["name"].Value : null,
                    [.. ClassFixture().Matches(declaration.Groups["bases"].Value).Select(fixture => fixture.Groups["name"].Value)]));
            }
        }

        return classes;
    }

    /// <summary>The collections whose classes run alone, after the parallel tests: <c>DisableParallelization = true</c>.</summary>
    public static IReadOnlySet<string> SerialCollections(IReadOnlyDictionary<string, string> files) =>
        files.Values.SelectMany(text => SerialCollection().Matches(text)).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every fixture that boots an AppHost: <c>AppHostFixture</c> and whatever derives from it, at any depth.</summary>
    public static IReadOnlySet<string> AppHostFixtures(IReadOnlyDictionary<string, string> files)
    {
        var bases = files.Values.SelectMany(text => ClassDeclaration().Matches(text))
            .Select(match => (Name: match.Groups["name"].Value, Base: FirstBase().Match(match.Groups["bases"].Value).Groups["name"].Value))
            .Where(pair => pair.Base.Length > 0)
            .ToList();
        var fixtures = new HashSet<string>(StringComparer.Ordinal) { "AppHostFixture" };

        // Until a pass adds nothing: a fixture can derive from a fixture that derives from AppHostFixture.
        bool added;
        do
        {
            added = false;
            foreach (var (name, parent) in bases)
            {
                added |= fixtures.Contains(parent) && fixtures.Add(name);
            }
        }
        while (added);

        return fixtures;
    }

    /// <summary>
    /// What is wrong between the classes <c>tests/shard-durations.json</c> weighs as running alone
    /// (<paramref name="weighedAlone"/>, the keys of its <c>alone</c>) and the sources: a class that boots an AppHost of
    /// its own beside the parallel tests, a class that runs alone and is weighed as if it did not, and one weighed as
    /// alone that is not. Empty when they agree.
    /// </summary>
    public static IReadOnlyList<string> AloneProblems(IReadOnlyCollection<string> weighedAlone, IReadOnlyDictionary<string, string> files)
    {
        var problems = new List<string>();
        var serial = SerialCollections(files);
        var fixtures = AppHostFixtures(files);
        var sharded = TestClasses(files).Where(type => ShardedProjects.Any(project => type.FullName.StartsWith($"{project}.", StringComparison.Ordinal))).ToList();
        var alone = sharded.Where(type => type.Collection is not null && serial.Contains(type.Collection)).Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);

        foreach (var type in sharded.Where(type => !alone.Contains(type.FullName)))
        {
            if (type.ClassFixtures.FirstOrDefault(fixtures.Contains) is { } fixture)
            {
                problems.Add($"{type.FullName} boots an AppHost of its own ({fixture}) beside the parallel tests. Put it in a collection with DisableParallelization (OwnStackRun), so its stack starts alone and its cost can be weighed.");
            }
        }

        foreach (var type in alone.Except(weighedAlone).Order(StringComparer.Ordinal))
        {
            problems.Add($"{type} runs alone, after the parallel tests, and tests/shard-durations.json weighs it as if it ran beside them, so its shard runs longer than planned. Add it to \"alone\" there (0 until the next refresh).");
        }

        foreach (var type in weighedAlone.Except(alone).Order(StringComparer.Ordinal))
        {
            problems.Add($"{type} is under \"alone\" in tests/shard-durations.json and is in no collection that runs alone. Remove it there.");
        }

        return problems;
    }

    [GeneratedRegex(@"^namespace\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileNamespace();

    // A class with the attributes on the lines above it and what follows its name on the same line: a primary
    // constructor and the base list.
    [GeneratedRegex(@"(?<attributes>(?:^[ \t]*\[[^\n]*\][ \t]*\n)*)^[ \t]*(?:public|internal)\s+(?<modifiers>(?:(?:sealed|abstract|static|partial)\s+)*)class\s+(?<name>\w+)(?<bases>[^\n{]*)", RegexOptions.Multiline)]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"^[ \t]*\[(?:Fact|Theory)\b", RegexOptions.Multiline)]
    private static partial Regex TestAttribute();

    [GeneratedRegex(@"\[Collection<(?<name>\w+)>\]")]
    private static partial Regex CollectionAttribute();

    [GeneratedRegex(@"IClassFixture<(?<name>\w+)>")]
    private static partial Regex ClassFixture();

    // The first type after the colon of a declaration, past a primary constructor's parameter list.
    [GeneratedRegex(@"^(?:\([^)]*\))?\s*:\s*(?<name>\w+)")]
    private static partial Regex FirstBase();

    [GeneratedRegex(@"\[CollectionDefinition\([^)\n]*DisableParallelization\s*=\s*true[^)\n]*\)\]\s*public\s+(?:sealed\s+)?class\s+(?<name>\w+)")]
    private static partial Regex SerialCollection();
}
