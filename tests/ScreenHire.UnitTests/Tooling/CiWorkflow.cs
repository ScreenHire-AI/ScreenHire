using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The rules <c>.github/</c> must keep, as checks over file text, so the tests run them on the repository and on the
/// same files edited in memory. Every job runs on the pinned Ubuntu and has a timeout; <c>Gate</c> needs every test
/// job and <c>tested-tree</c>, and every job but those two answers to <c>tested-tree</c>; nothing reads a secret or runs
/// on a self-hosted runner (the repository's logs are public, and a self-hosted runner would run any pull request's
/// code); the images CI pulls before the tests are the ones <c>apps/apphost/AppHost.cs</c> runs; and a pull request
/// starts no more jobs at once than the account runs at once (<see cref="ConcurrentJobs"/>).
/// <see cref="Problems"/> returns what breaks a rule, empty when nothing does.
/// </summary>
internal static partial class CiWorkflow
{
    public const string PinnedRunner = "ubuntu-24.04";

    public const string CiPath = ".github/workflows/ci.yml";

    /// <summary>
    /// The workflow that fills main's caches after a push that changed what a cache key is made from. ci.yml's own jobs
    /// save the caches, and ci.yml skips them on a push whose tree a pull request already passed.
    /// </summary>
    public const string CachesPath = ".github/workflows/caches.yml";

    public const string SetupActionPath = ".github/actions/setup/action.yml";

    /// <summary>
    /// The job that says whether a push to main landed a tree its pull request already tested
    /// (<c>scripts/tested-tree.mjs</c>). The gate needs it for the answer, and every other job needs it to be skipped.
    /// </summary>
    public const string TestedTreeJob = "tested-tree";

    /// <summary>What every job but <see cref="TestedTreeJob"/> and the gate must say, or it runs when its tree was tested.</summary>
    public const string SkipCondition = "needs.tested-tree.outputs.tested != 'true'";

    /// <summary>
    /// How many jobs the account runs at once: GitHub Free's limit for an organization, and what the runs of 2026-11-07 to
    /// 2026-11-18 peaked at on every day but two short bursts (docs/process/testing.md, "Time"). A pull request that starts
    /// more leaves a job waiting for a runner even when nothing else is running, as 21 did until 2026-11-18. Adding a job
    /// means giving one up (.claude/rules/test-value.md).
    /// </summary>
    public const int ConcurrentJobs = 20;

    /// <summary>
    /// Jobs outside the gate's list on purpose: the gate itself, Lighthouse, which only reports, and the job that decides
    /// whether the others run.
    /// </summary>
    public static readonly string[] OutsideGate = ["gate", "lighthouse", TestedTreeJob];

    public sealed record Job(
        string Id, string? Name, IReadOnlyList<string> RunsOn, string? TimeoutMinutes, IReadOnlyList<string> Needs, string? If,
        IReadOnlyList<string> Shard, IReadOnlyList<string> Shards, IReadOnlyDictionary<string, string> Permissions, int? Legs);

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

        if (jobs.Where(StartsOnAPullRequest).FirstOrDefault(job => job.Legs is null) is { } uncounted)
        {
            problems.Add($"{CiPath}: job '{uncounted.Id}' has a matrix this check cannot count (include or exclude), so the jobs a pull request starts are unknown");
        }
        else if (PullRequestJobs(ci) is var started && started > ConcurrentJobs)
        {
            problems.Add($"{CiPath}: a pull request starts {started} jobs at once, over the account's {ConcurrentJobs}, so one always waits for a runner: give a job up before adding one (.claude/rules/test-value.md)");
        }

        problems.AddRange(CacheFillProblems(gitHubFiles));

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
                Permissions(Child(job, "permissions")),
                Legs(Child(Child(job, "strategy"), "matrix")));
        }).ToList();
    }

    /// <summary>
    /// How many jobs a pull request run starts at once: every job that answers to <see cref="TestedTreeJob"/> with the skip
    /// condition, a matrix counted once per leg. That leaves out the job that runs on a push only and the gate, which
    /// starts when the others have ended.
    /// </summary>
    public static int PullRequestJobs(string workflow) => Jobs(workflow).Where(StartsOnAPullRequest).Sum(job => job.Legs ?? 0);

    private static bool StartsOnAPullRequest(Job job) => job.If?.Contains(SkipCondition, StringComparison.Ordinal) == true;

    /// <summary>
    /// The jobs a matrix makes: the product of its lists' lengths, 1 with no matrix, and null when it has <c>include</c> or
    /// <c>exclude</c>, which this does not work out.
    /// </summary>
    private static int? Legs(YamlNode? matrix)
    {
        if (matrix is not YamlMappingNode mapping)
        {
            return 1;
        }

        var legs = 1;
        foreach (var (key, value) in mapping.Children)
        {
            if (Scalar(key) is "include" or "exclude" || value is not YamlSequenceNode list)
            {
                return null;
            }

            legs *= list.Children.Count;
        }

        return legs;
    }

    /// <summary>
    /// What a push must change for a cache key to change: the files the setup action's keys hash, the lockfile behind
    /// setup-node's own pnpm cache, the action that defines the keys and the workflow that fills them.
    /// </summary>
    public static IReadOnlySet<string> CacheKeyFiles(IReadOnlyDictionary<string, string> gitHubFiles)
    {
        var files = new HashSet<string>(StringComparer.Ordinal) { ".github/actions/setup/**", CachesPath };
        if (!gitHubFiles.TryGetValue(SetupActionPath, out var action))
        {
            return files;
        }

        foreach (Match hash in HashFilesCall().Matches(action))
        {
            files.UnionWith(QuotedArgument().Matches(hash.Groups["arguments"].Value).Select(argument => argument.Groups["value"].Value));
        }

        if (PnpmStoreCache().IsMatch(action))
        {
            files.Add("pnpm-lock.yaml");
        }

        return files;
    }

    /// <summary>
    /// What is wrong with the workflow that fills main's caches: it must run on a push to main that changes any file a
    /// cache key is made from, never on a pull request (its jobs would take slots from every one), and set up through the
    /// setup action, so the keys it saves under are the ones the test jobs restore.
    /// </summary>
    private static List<string> CacheFillProblems(IReadOnlyDictionary<string, string> gitHubFiles)
    {
        var problems = new List<string>();
        if (!gitHubFiles.TryGetValue(CachesPath, out var caches))
        {
            problems.Add($"{CachesPath} does not exist, so nothing saves main's caches after a dependency change: {CiPath} skips its jobs on a push a pull request already tested");
            return problems;
        }

        var on = Child(Root(caches), "on");
        foreach (var trigger in new[] { "pull_request", "pull_request_target" })
        {
            if (Child(on, trigger) is not null || Strings(on).Contains(trigger))
            {
                problems.Add($"{CachesPath} must not run on a pull request ({trigger}): its jobs would take the account's slots from every one");
            }
        }

        var filled = Strings(Child(Child(on, "push"), "paths")).ToHashSet(StringComparer.Ordinal);
        var hashed = CacheKeyFiles(gitHubFiles);
        foreach (var file in hashed.Except(filled).Order(StringComparer.Ordinal))
        {
            problems.Add($"{CachesPath} does not fill main's caches when {file} changes, and a cache key is made from it ({SetupActionPath}): add it to on.push.paths");
        }

        foreach (var file in filled.Except(hashed).Order(StringComparer.Ordinal))
        {
            problems.Add($"{CachesPath} fills main's caches when {file} changes, and no cache key is made from it ({SetupActionPath}): remove it from on.push.paths");
        }

        if (!caches.Contains("uses: ./.github/actions/setup", StringComparison.Ordinal))
        {
            problems.Add($"{CachesPath} must set up through .github/actions/setup, or it saves under keys the test jobs do not restore");
        }

        return problems;
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

    [GeneratedRegex(@"hashFiles\((?<arguments>[^)]*)\)")]
    private static partial Regex HashFilesCall();

    [GeneratedRegex(@"'(?<value>[^']+)'")]
    private static partial Regex QuotedArgument();

    // setup-node's own cache of the pnpm store, whose key is the lockfile's hash.
    [GeneratedRegex(@"^\s*cache:\s*pnpm\s*$", RegexOptions.Multiline)]
    private static partial Regex PnpmStoreCache();
}
