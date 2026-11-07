using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The rules <c>.github/</c> must keep, as checks over file text, so the tests run them on the repository and on the
/// same files edited in memory. Every job runs on the pinned Ubuntu and has a timeout; <c>Gate</c> needs every test
/// job and <c>tested-tree</c>, and every job but those two answers to <c>tested-tree</c>; nothing reads a secret or runs
/// on a self-hosted runner (the repository's logs are public, and a self-hosted runner would run any pull request's
/// code); and the images CI pulls before the tests are the ones
/// <c>apps/apphost/AppHost.cs</c> runs. <see cref="Problems"/> returns what breaks a rule, empty when nothing does.
/// </summary>
internal static partial class CiWorkflow
{
    public const string PinnedRunner = "ubuntu-24.04";

    public const string CiPath = ".github/workflows/ci.yml";

    /// <summary>
    /// The job that says whether a push to main landed a tree its pull request already tested
    /// (<c>scripts/tested-tree.mjs</c>). The gate needs it for the answer, and every other job needs it to be skipped.
    /// </summary>
    public const string TestedTreeJob = "tested-tree";

    /// <summary>What every job but <see cref="TestedTreeJob"/> and the gate must say, or it runs when its tree was tested.</summary>
    public const string SkipCondition = "needs.tested-tree.outputs.tested != 'true'";

    /// <summary>
    /// Jobs outside the gate's list on purpose: the gate itself, Lighthouse, which only reports, and the job that decides
    /// whether the others run.
    /// </summary>
    public static readonly string[] OutsideGate = ["gate", "lighthouse", TestedTreeJob];

    public sealed record Job(
        string Id, string? Name, IReadOnlyList<string> RunsOn, string? TimeoutMinutes, IReadOnlyList<string> Needs, string? If,
        IReadOnlyList<string> Shard, IReadOnlyList<string> Shards, IReadOnlyDictionary<string, string> Permissions);

    /// <summary>Every file under <c>.github/</c> in the repository, by its '/'-separated repo path.</summary>
    public static Dictionary<string, string> RepositoryGitHubFiles()
    {
        var root = RepositoryFiles.PathTo();
        return Directory.EnumerateFiles(RepositoryFiles.PathTo(".github"), "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.ReadAllText(path).ReplaceLineEndings("\n"),
                StringComparer.Ordinal);
    }

    public static string RepositoryAppHost() =>
        File.ReadAllText(RepositoryFiles.PathTo("apps", "apphost", "AppHost.cs")).ReplaceLineEndings("\n");

    public static IReadOnlyList<string> Problems(IReadOnlyDictionary<string, string> gitHubFiles, string appHost)
    {
        var problems = new List<string>();
        foreach (var (path, text) in gitHubFiles)
        {
            if (SecretReference().IsMatch(text))
            {
                problems.Add($"{path} reads a secret; CI runs without any, because its logs and artifacts are public");
            }
        }

        foreach (var (path, text) in gitHubFiles.Where(file => IsWorkflow(file.Key)))
        {
            foreach (var job in Jobs(text))
            {
                if (job.RunsOn.Any(label => label.Equals("self-hosted", StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{path}: job '{job.Id}' runs on a self-hosted runner");
                }
                else if (job.RunsOn is not [PinnedRunner])
                {
                    problems.Add($"{path}: job '{job.Id}' runs on [{string.Join(", ", job.RunsOn)}], not {PinnedRunner}");
                }

                if (!int.TryParse(job.TimeoutMinutes, out var minutes) || minutes <= 0)
                {
                    problems.Add($"{path}: job '{job.Id}' has no positive timeout-minutes");
                }

                // A matrix of shards must run every one of them: a number left out of the list skips its tests silently.
                if (job.Shard.Count > 0 || job.Shards.Count > 0)
                {
                    var expected = job.Shards is [var total] && int.TryParse(total, out var count) && count > 0
                        ? Enumerable.Range(1, count).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList()
                        : null;
                    if (expected is null || !job.Shard.SequenceEqual(expected))
                    {
                        problems.Add($"{path}: job '{job.Id}' runs shards [{string.Join(", ", job.Shard)}] of [{string.Join(", ", job.Shards)}], not 1 to its one shards value");
                    }
                }
            }
        }

        if (!gitHubFiles.TryGetValue(CiPath, out var ci))
        {
            problems.Add($"{CiPath} does not exist");
            return problems;
        }

        var jobs = Jobs(ci);
        if (jobs.SingleOrDefault(job => job.Id == "gate") is not { } gate)
        {
            problems.Add($"{CiPath} has no 'gate' job");
        }
        else
        {
            var testJobs = jobs.Select(job => job.Id).Except(OutsideGate).Order(StringComparer.Ordinal).ToList();
            var gateNeeds = testJobs.Append(TestedTreeJob).Order(StringComparer.Ordinal).ToList();
            if (!gate.Needs.Order(StringComparer.Ordinal).SequenceEqual(gateNeeds))
            {
                problems.Add($"{CiPath}: gate needs [{string.Join(", ", gate.Needs)}], not every test job and {TestedTreeJob} [{string.Join(", ", gateNeeds)}]");
            }

            if (gate.Name != "Gate")
            {
                problems.Add($"{CiPath}: the gate job's name is '{gate.Name}', not 'Gate', the check branch protection requires");
            }

            if (gate.If?.Contains("always()", StringComparison.Ordinal) != true)
            {
                problems.Add($"{CiPath}: the gate job must run with if: always(), or a failed job skips it and nothing fails");
            }
        }

        if (jobs.SingleOrDefault(job => job.Id == TestedTreeJob) is not { } testedTree)
        {
            problems.Add($"{CiPath} has no '{TestedTreeJob}' job");
        }
        else
        {
            if (testedTree.If?.Contains("github.event_name == 'push'", StringComparison.Ordinal) != true)
            {
                problems.Add($"{CiPath}: the {TestedTreeJob} job must run only on a push, where a tree can already have been tested");
            }

            foreach (var scope in new[] { "checks", "contents", "pull-requests" })
            {
                if (!testedTree.Permissions.TryGetValue(scope, out var level) || level != "read")
                {
                    problems.Add($"{CiPath}: the {TestedTreeJob} job needs {scope}: read to read the pull request's checks");
                }
            }

            // A job that does not wait for the answer, or ignores it, runs its tests on a tree that was already tested.
            foreach (var job in jobs.Where(job => job.Id is not (TestedTreeJob or "gate")))
            {
                if (!job.Needs.Contains(TestedTreeJob))
                {
                    problems.Add($"{CiPath}: job '{job.Id}' does not need {TestedTreeJob}, so it cannot be skipped for a tested tree");
                }
                else if (job.If is null || !job.If.Contains(SkipCondition, StringComparison.Ordinal) || !job.If.Contains("!cancelled()", StringComparison.Ordinal))
                {
                    problems.Add($"{CiPath}: job '{job.Id}' must run with if: !cancelled() && {SkipCondition}");
                }
            }
        }

        var pulled = PulledImages(ci);
        var used = AppHostImages(appHost);
        foreach (var image in used.Except(pulled))
        {
            problems.Add($"AppHost.cs runs {image}, which {CiPath} does not pull (APPHOST_IMAGES)");
        }

        foreach (var image in pulled.Except(used))
        {
            problems.Add($"{CiPath} pulls {image}, which AppHost.cs does not run");
        }

        return problems;
    }

    /// <summary>The jobs of a workflow. A file that is not valid YAML throws, so a broken workflow fails here first.</summary>
    public static IReadOnlyList<Job> Jobs(string workflow)
    {
        var root = Root(workflow);
        if (Child(root, "jobs") is not YamlMappingNode jobs)
        {
            return [];
        }

        return jobs.Children.Select(entry =>
        {
            var job = (YamlMappingNode)entry.Value;
            return new Job(
                ((YamlScalarNode)entry.Key).Value!,
                Scalar(Child(job, "name")),
                Strings(Child(job, "runs-on")),
                Scalar(Child(job, "timeout-minutes")),
                Strings(Child(job, "needs")),
                Scalar(Child(job, "if")),
                Strings(Child(Child(Child(job, "strategy"), "matrix"), "shard")),
                Strings(Child(Child(Child(job, "strategy"), "matrix"), "shards")),
                Permissions(Child(job, "permissions")));
        }).ToList();
    }

    /// <summary>The workflow's <c>env.APPHOST_IMAGES</c>, normalized like <see cref="AppHostImages"/>.</summary>
    public static IReadOnlySet<string> PulledImages(string workflow)
    {
        var list = Scalar(Child(Child(Root(workflow), "env"), "APPHOST_IMAGES")) ?? "";
        return list.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(reference =>
        {
            var colon = reference.LastIndexOf(':');
            return colon > reference.LastIndexOf('/')
                ? Normalize(null, reference[..colon], reference[(colon + 1)..])
                : Normalize(null, reference, "latest");
        }).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every image <c>AppHost.cs</c> names: <c>AddContainer(name, "image", "tag")</c> and <c>WithImage("image", "tag")</c>,
    /// with a <c>WithImageRegistry("registry")</c> in the same statement. Comment lines are skipped.
    /// </summary>
    public static IReadOnlySet<string> AppHostImages(string appHost)
    {
        var code = string.Join('\n', appHost.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var images = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ImageCall().Matches(code))
        {
            var end = code.IndexOf(';', match.Index + match.Length);
            var statement = code[(match.Index + match.Length)..(end < 0 ? code.Length : end)];
            var registry = ImageRegistryCall().Match(statement) is { Success: true } found ? found.Groups["registry"].Value : null;
            images.Add(Normalize(registry, match.Groups["image"].Value, match.Groups["tag"].Value));
        }

        return images;
    }

    /// <summary>Docker's short form: no <c>docker.io/</c>, no <c>library/</c>, so both lists compare as Docker shows them.</summary>
    private static string Normalize(string? registry, string image, string tag)
    {
        var name = registry is null or "" ? image : $"{registry}/{image}";
        name = name.StartsWith("docker.io/", StringComparison.Ordinal) ? name["docker.io/".Length..] : name;
        name = name.StartsWith("library/", StringComparison.Ordinal) ? name["library/".Length..] : name;
        return $"{name}:{tag}";
    }

    private static bool IsWorkflow(string path) =>
        path.StartsWith(".github/workflows/", StringComparison.Ordinal)
        && (path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal));

    private static YamlMappingNode Root(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents is [{ RootNode: YamlMappingNode root }]
            ? root
            : throw new InvalidOperationException("A workflow must be one YAML document whose root is a mapping.");
    }

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child : null;

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;

    private static Dictionary<string, string> Permissions(YamlNode? node) =>
        node is YamlMappingNode mapping
            ? mapping.Children
                .Where(entry => entry.Key is YamlScalarNode && entry.Value is YamlScalarNode)
                .ToDictionary(entry => ((YamlScalarNode)entry.Key).Value!, entry => ((YamlScalarNode)entry.Value).Value ?? "", StringComparer.Ordinal)
            : [];

    private static List<string> Strings(YamlNode? node) => node switch
    {
        YamlScalarNode scalar => [scalar.Value ?? ""],
        YamlSequenceNode sequence => [.. sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value ?? "")],
        _ => [],
    };

    /// <summary>
    /// <c>secrets.NAME</c>, <c>secrets['NAME']</c> or <c>toJSON(secrets)</c> in an expression, or <c>secrets: inherit</c>
    /// on a reusable-workflow call.
    /// </summary>
    [GeneratedRegex(@"\bsecrets\s*[.\[)]|\bsecrets\s*:\s*inherit\b")]
    private static partial Regex SecretReference();

    [GeneratedRegex(@"(?:AddContainer\(\s*[^,()]+,|WithImage\()\s*""(?<image>[^""]+)""\s*,\s*""(?<tag>[^""]+)""\s*\)")]
    private static partial Regex ImageCall();

    [GeneratedRegex(@"WithImageRegistry\(\s*""(?<registry>[^""]+)""\s*\)")]
    private static partial Regex ImageRegistryCall();
}
