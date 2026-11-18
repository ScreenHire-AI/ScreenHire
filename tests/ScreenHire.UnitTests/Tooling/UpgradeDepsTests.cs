using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// <c>scripts/upgrade-deps.mjs</c>, the mechanical half of <c>/upgrade-deps</c>. A wrong answer here files the wrong
/// work for an unattended agent: a downgrade, a patch held behind a framework major, a lockstep group installed half way,
/// a conflict nobody saw until restore, or research thrown away and paid for again. Each case drives one exported
/// function from <c>node</c> with fixed registry data, so no case reads the network.
/// </summary>
public sealed class UpgradeDepsTests
{
    [Fact]
    public void The_research_cache_is_gitignored_so_briefs_never_reach_the_public_repo()
    {
        File.ReadAllLines(RepositoryFiles.PathTo(".gitignore")).Should().Contain(".upgrade-deps/");
        Evaluate("m.CACHE_FOLDER").GetString().Should().Be(".upgrade-deps");
    }

    [Fact]
    public void A_prerelease_install_never_moves_down_to_a_lower_latest()
    {
        var targets = Evaluate("""
            m.chooseTargets({ installed: '1.2.0-beta.18', distTags: { latest: '1.1.0' },
              versions: { '1.1.0': {}, '1.2.0-beta.18': {}, '1.2.0-beta.19': {}, '1.3.0-beta.1': {} } })
            """);

        targets.GetProperty("latest").GetString().Should().Be("1.2.0-beta.19");
    }

    [Fact]
    public void A_prerelease_install_takes_its_stable_release_and_not_the_next_lines_beta()
    {
        var targets = Evaluate("""
            m.chooseTargets({ installed: '1.2.0-beta.18', distTags: { latest: '1.2.0' },
              versions: { '1.2.0-beta.19': {}, '1.2.0': {}, '1.3.0-beta.1': {} } })
            """);

        targets.GetProperty("latest").GetString().Should().Be("1.2.0");
    }

    [Fact]
    public void A_stable_install_stays_off_prereleases_and_never_passes_latest()
    {
        var targets = Evaluate("""
            m.chooseTargets({ installed: '1.0.0', distTags: { latest: '1.1.0' },
              versions: { '1.1.0': {}, '1.2.0': {}, '2.0.0-beta.1': {} } })
            """);

        targets.GetProperty("latest").GetString().Should().Be("1.1.0");
    }

    [Fact]
    public void Deprecated_and_unlisted_releases_are_never_targets()
    {
        var targets = Evaluate("""
            m.chooseTargets({ installed: '1.0.0',
              versions: { '1.1.0': { deprecated: 'broken' }, '1.0.2': { listed: false }, '1.0.1': {} } })
            """);

        targets.GetProperty("latest").GetString().Should().Be("1.0.1");
    }

    [Fact]
    public void A_major_leaves_the_newest_release_on_our_major_as_the_line()
    {
        // .NET 11 shipped while we run 10: the 10.0.x patch must stay reachable on its own.
        var targets = Evaluate("""
            m.chooseTargets({ installed: '10.0.14', versions: { '10.0.15': {}, '11.0.0': {}, '11.0.1-rc.1': {} } })
            """);

        targets.GetProperty("latest").GetString().Should().Be("11.0.0");
        targets.GetProperty("line").GetString().Should().Be("10.0.15");
    }

    [Fact]
    public void A_zero_major_minor_counts_as_a_major()
    {
        var targets = Evaluate("m.chooseTargets({ installed: '0.3.0', versions: { '0.3.1': {}, '0.4.0': {} } })");

        targets.GetProperty("latest").GetString().Should().Be("0.4.0");
        targets.GetProperty("line").GetString().Should().Be("0.3.1");
    }

    [Theory]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-RC.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-alpha.10", "1.0.0-alpha.9")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    [InlineData("9.2.1040", "9.2.1039")]
    public void Versions_compare_under_NuGet_rules_which_cover_npm_too(string higher, string lower)
    {
        Evaluate($"m.compareVersions('{higher}', '{lower}')").GetInt32().Should().Be(1);
        Evaluate($"m.compareVersions('{lower}', '{higher}')").GetInt32().Should().Be(-1);
    }

    [Theory]
    [InlineData("6.0.3", "7.0.2", "major")]
    [InlineData("0.3.0", "0.4.0", "major")]
    [InlineData("0.0.1", "0.0.2", "major")]
    [InlineData("0.9.10", "0.9.12", "patch")]
    [InlineData("3.32.0", "3.34.0", "minor")]
    [InlineData("10.0.14", "10.0.15", "patch")]
    [InlineData("1.0.0-rc.1", "1.0.0", "prerelease")]
    public void The_kind_of_a_move_follows_semver(string installed, string target, string kind)
    {
        Evaluate($"m.changeKind('{installed}', '{target}')").GetString().Should().Be(kind);
    }

    [Theory]
    [InlineData("{ name: 'Aspire.Hosting.PostgreSQL', ecosystem: 'nuget', property: 'AspireVersion' }", "aspire")]
    [InlineData("{ name: 'aspire.cli', ecosystem: 'nuget' }", "aspire")]
    [InlineData("{ name: 'CommunityToolkit.Aspire.Hosting.MailPit', ecosystem: 'nuget' }", "aspire")]
    [InlineData("{ name: 'Microsoft.EntityFrameworkCore', ecosystem: 'nuget', property: 'DotNetVersion' }", "dotnet")]
    [InlineData("{ name: 'dotnet-ef', ecosystem: 'nuget' }", "dotnet")]
    [InlineData("{ name: 'WolverineFx.Postgresql', ecosystem: 'nuget' }", "wolverine")]
    [InlineData("{ name: 'Microsoft.Extensions.AI.OpenAI', ecosystem: 'nuget' }", "extensions-ai")]
    [InlineData("{ name: 'Microsoft.Extensions.Http.Resilience', ecosystem: 'nuget' }", "microsoft.extensions.http.resilience")]
    [InlineData("{ name: '@tiptap/pm', ecosystem: 'npm' }", "tiptap")]
    [InlineData("{ name: '@tailwindcss/vite', ecosystem: 'npm' }", "tailwind")]
    [InlineData("{ name: '@types/node', ecosystem: 'npm' }", "node")]
    [InlineData("{ name: '@sveltejs/kit', ecosystem: 'npm' }", "sveltejs-kit")]
    [InlineData("{ name: 'typescript', ecosystem: 'npm' }", "typescript")]
    [InlineData("{ name: 'aspire.cli', ecosystem: 'npm' }", "aspire.cli")]
    public void Packages_that_must_match_share_a_group(string record, string group)
    {
        Evaluate($"m.groupOf({record})").GetString().Should().Be(group);
    }

    [Fact]
    public void A_release_that_changes_nothing_raises_no_signal()
    {
        var facts = "{ dependencies: { a: '^1' }, installScripts: [], licence: 'MIT', publisher: 'x', attested: true }";

        Evaluate($"m.supplyChainSignals({facts}, {facts})").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void The_shape_of_the_axios_compromise_raises_two_signals()
    {
        var signals = Strings(Evaluate("""
            m.supplyChainSignals(
              { dependencies: { 'follow-redirects': '^1' }, attested: true, publisher: 'maintainer' },
              { dependencies: { 'follow-redirects': '^1', 'plain-crypto-js': '^4' }, attested: false, publisher: 'maintainer' })
            """));

        signals.Should().BeEquivalentTo("new dependencies (1): plain-crypto-js", "provenance dropped");
    }

    [Fact]
    public void Code_that_runs_at_install_or_build_time_raises_a_signal()
    {
        var signals = Strings(Evaluate("""
            m.supplyChainSignals(
              { installScripts: [], gypfile: false, buildFiles: [] },
              { installScripts: ['postinstall'], gypfile: true, buildFiles: ['buildTransitive/Package.targets'] })
            """));

        signals.Should().BeEquivalentTo(
            "new install scripts: postinstall", "a binding.gyp build appeared", "new build-time files (1): buildTransitive/Package.targets");
    }

    [Fact]
    public void A_licence_change_raises_a_signal_even_to_no_declared_licence()
    {
        Strings(Evaluate("m.supplyChainSignals({ licence: 'MIT' }, { licence: 'Apache-2.0' })"))
            .Should().Equal("licence changed: MIT to Apache-2.0");
        Strings(Evaluate("m.supplyChainSignals({ licence: 'MIT' }, { licence: null })"))
            .Should().Equal("licence changed: MIT to none declared");
    }

    [Fact]
    public void A_long_list_stays_one_line_and_a_missing_version_compares_nothing()
    {
        var signals = Strings(Evaluate("""
            m.supplyChainSignals({ dependencies: {} },
              { dependencies: Object.fromEntries(Array.from({ length: 20 }, (_, index) => [`platform-${index}`, '1'])) })
            """));

        signals.Should().ContainSingle().Which.Should().StartWith("new dependencies (20): platform-0, platform-1");
        Evaluate("m.supplyChainSignals(undefined, { licence: 'MIT' })").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("tailwind", "[{ target: '4.3.5' }, { target: '4.3.4' }]", false)]
    [InlineData("tailwind-major", "[{ target: '5.0.0' }, { target: '5.0.1' }]", false)]
    [InlineData("aspire", "[{ target: '17.0.1' }, { target: '17.0.0' }]", true)]
    [InlineData("aspire-major", "[{ target: '17.0.0' }, { target: '13.6.1' }]", false)]
    [InlineData("fontsource", "[{ target: '5.3.1' }, { target: '5.4.0' }]", true)]
    [InlineData("other", "[{ target: '1.0.0', property: 'P' }, { target: '1.0.1', property: 'P' }]", false)]
    public void Targets_agree_as_their_group_requires(string group, string members, bool agree)
    {
        Evaluate($"m.targetsAgree('{group}', {members})").GetBoolean().Should().Be(agree);
    }

    [Theory]
    [InlineData("10.0.14", "[10.0.15, )", false)]
    [InlineData("10.0.15", "[10.0.15, )", true)]
    [InlineData("10.0.5", "10.0.4", true)]
    [InlineData("11.0.0", "[10.0.4, 11.0.0)", false)]
    [InlineData("10.9.0", "[10.0.4, 11.0.0)", true)]
    [InlineData("1.2.0", "[1.2]", true)]
    [InlineData("1.2.1", "[1.2]", false)]
    [InlineData("3.0.0", "(, 3.0]", true)]
    [InlineData("3.0.1", "(, 3.0]", false)]
    [InlineData("2.0.0", "(1.0, )", true)]
    [InlineData("1.0.0", "(1.0, )", false)]
    public void NuGet_ranges_bound_as_written(string version, string range, bool allowed)
    {
        Evaluate($"m.satisfiesNuGetRange('{version}', '{range}')").GetBoolean().Should().Be(allowed);
    }

    [Fact]
    public void A_NuGet_range_it_cannot_read_is_no_answer_rather_than_a_conflict()
    {
        Evaluate("m.satisfiesNuGetRange('1.0.0', 'not a range')").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void A_major_its_dependents_cannot_accept_is_a_conflict()
    {
        var conflicts = Strings(Evaluate("""
            m.newConflicts(
              { vite: { name: 'vite', version: '8.3.4' }, '@sveltejs/vite-plugin-svelte': { name: '@sveltejs/vite-plugin-svelte', version: '7.3.1' } },
              { vite: { name: 'vite', version: '9.0.0' } },
              (key) => (key === '@sveltejs/vite-plugin-svelte' ? { vite: '^8.0.0' } : {}))
            """));

        conflicts.Should().Equal("@sveltejs/vite-plugin-svelte@7.3.1 needs vite ^8.0.0, planned 9.0.0");
    }

    [Fact]
    public void A_conflict_the_tree_already_has_is_not_news_and_a_prerelease_a_caret_names_is_accepted()
    {
        var requirements = "(key) => (key === 'a' ? { b: '^1.0.0', c: '^2.0.0-beta.1' } : {})";
        var installed = "{ a: { name: 'a', version: '1.0.0' }, b: { name: 'b', version: '0.9.0' }, c: { name: 'c', version: '2.0.0-beta.1' } }";

        Evaluate($"m.newConflicts({installed}, {{ c: {{ name: 'c', version: '2.0.0-beta.5' }} }}, {requirements})")
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void A_NuGet_floor_above_a_central_pin_is_a_conflict_until_that_pin_moves_too()
    {
        // Aspire 17 needs Microsoft.Extensions.Hosting 10.0.15, and DotNetVersion pins 10.0.14.
        var installed = """
            { 'nuget:aspire.hosting.postgresql': { name: 'Aspire.Hosting.PostgreSQL', version: '13.6.5' },
              'nuget:microsoft.extensions.hosting': { name: 'Microsoft.Extensions.Hosting', version: '10.0.14' } }
            """;
        var requirements = "(key, version) => (key === 'nuget:aspire.hosting.postgresql' && version === '17.0.0' ? { 'Microsoft.Extensions.Hosting': '[10.0.15, )' } : {})";
        var aspire = "'nuget:aspire.hosting.postgresql': { name: 'Aspire.Hosting.PostgreSQL', version: '17.0.0' }";

        Strings(Evaluate($"m.newConflicts({installed}, {{ {aspire} }}, {requirements})"))
            .Should().Equal("Aspire.Hosting.PostgreSQL@17.0.0 needs Microsoft.Extensions.Hosting [10.0.15, ), planned 10.0.14");
        Evaluate($"m.newConflicts({installed}, {{ {aspire}, 'nuget:microsoft.extensions.hosting': {{ name: 'Microsoft.Extensions.Hosting', version: '10.0.15' }} }}, {requirements})")
            .GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("['net8.0', 'net10.0', 'net11.0', '.NETStandard2.0']", "net10.0")]
    [InlineData("['net8.0', 'net11.0']", "net8.0")]
    [InlineData("['.NETStandard2.0', '.NETFramework4.6.2']", ".NETStandard2.0")]
    [InlineData("['netstandard2.0', 'netstandard2.1']", "netstandard2.1")]
    public void The_dependency_group_is_the_one_restore_would_pick(string frameworks, string picked)
    {
        Evaluate($"m.nearestDependencyGroup({frameworks}.map((targetFramework) => ({{ targetFramework }})), 'net10.0').targetFramework")
            .GetString().Should().Be(picked);
    }

    [Fact]
    public void A_package_only_for_a_newer_framework_has_no_dependency_group_for_us()
    {
        Evaluate("m.nearestDependencyGroup([{ targetFramework: 'net11.0' }], 'net10.0')").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Build_time_files_ignore_the_framework_folder_so_a_retarget_is_not_news()
    {
        var files = Strings(Evaluate("""
            m.buildFilesOf([
              { fullName: 'buildTransitive/net10.0/Package.props' }, { fullName: 'buildTransitive/net11.0/Package.props' },
              { fullName: 'lib/net10.0/Package.dll' }, { fullName: 'analyzers/dotnet/cs/Package.Analyzers.dll' },
              { fullName: 'tools/install.ps1' }, { fullName: 'build/Package.targets' }, { fullName: 'README.md' }])
            """));

        files.Should().Equal("analyzers/Package.Analyzers.dll", "build/Package.targets", "buildTransitive/Package.props", "tools/install.ps1");
    }

    private const string Brief = "{ targets: { a: '2.0.0' }, evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'ready' }";
    private const string BlockedBrief = "{ targets: { a: '2.0.0' }, evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'blocked', blocker: { blockedOn: { 'nuget:Waits.On': '1.0.0' } } }";

    [Theory]
    [InlineData("undefined", "{ a: '2.0.0' }", "{}", false, "research")]
    [InlineData(Brief, "{ a: '2.0.0' }", "{}", false, "reuse")]
    [InlineData(Brief, "{ a: '2.0.1' }", "{}", false, "delta")]
    [InlineData(Brief, "{ a: '2.0.0' }", "{}", true, "research")]
    [InlineData("{ targets: { a: '2.0.0' }, evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'ready', draft: true }", "{ a: '2.0.0' }", "{}", false, "research")]
    [InlineData("{ targets: { a: '2.0.0' }, evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'ready', task: { displayId: 4 } }", "{ a: '2.0.0' }", "{}", false, "filed")]
    [InlineData(BlockedBrief, "{ a: '2.0.0' }", "{ 'nuget:Waits.On': '1.0.0' }", false, "blocked")]
    [InlineData(BlockedBrief, "{ a: '2.0.0' }", "{ 'nuget:Waits.On': '1.1.0' }", false, "recheck")]
    public void Research_is_reused_until_a_version_moves(string brief, string targets, string watched, bool refresh, string state)
    {
        Evaluate($"m.researchState({{ brief: {brief}, targets: {targets}, watchedVersions: {watched}, refresh: {(refresh ? "true" : "false")} }})")
            .GetString().Should().Be(state);
    }

    [Fact]
    public void A_blocker_watches_the_newest_release_and_a_NuGet_package_has_no_dist_tags()
    {
        Evaluate("m.watchedVersion('10.0.3', { versions: { '10.0.3': {}, '10.0.4': {}, '11.0.0-rc.1': {} } })").GetString().Should().Be("10.0.4");
        Evaluate("m.watchedVersion('not-a-version', { versions: { '1.0.0': {}, '2.0.0': {}, '3.0.0-rc.1': {} } })").GetString().Should().Be("2.0.0");
        Evaluate("m.watchedVersion('1.2.0-beta.3', { distTags: { latest: '1.1.0' }, versions: { '1.1.0': {}, '1.2.0-beta.4': {} } })").GetString().Should().Be("1.2.0-beta.4");
    }

    [Fact]
    public void A_recorded_blocker_drops_the_task_link_so_the_next_run_files_it_again_once_cleared()
    {
        var brief = Evaluate($"m.withBlocker({{ ...{Brief}, task: {{ displayId: 9 }} }}, {{ reason: 'r', recheck: 'c', blockedOn: {{}} }}, Date.parse('2026-11-18T00:00:00Z'))");

        brief.GetProperty("verdict").GetString().Should().Be("blocked");
        brief.GetProperty("task").ValueKind.Should().Be(JsonValueKind.Null);
        brief.GetProperty("evaluatedAt").GetString().Should().Be("2026-11-18T00:00:00.000Z");
    }

    // Two property members on 10.0.14, the local tool beside them, and a registry where 10.0.15 and 11.0.0 both exist.
    private const string DotNetInventory = """
        m.buildInventory({
          declared: [
            { name: 'Microsoft.EntityFrameworkCore', ecosystem: 'nuget', installed: '10.0.14', locations: ['Directory.Packages.props'], property: 'DotNetVersion' },
            { name: 'Microsoft.Extensions.Hosting', ecosystem: 'nuget', installed: '10.0.14', locations: ['Directory.Packages.props'], property: 'DotNetVersion' },
            { name: 'dotnet-ef', ecosystem: 'nuget', installed: '10.0.14', locations: ['dotnet-tools.json'], tool: true },
            { name: 'Aspire.Hosting.PostgreSQL', ecosystem: 'nuget', installed: '13.6.5', locations: ['Directory.Packages.props'], property: 'AspireVersion' },
            { name: 'CommunityToolkit.Aspire.Hosting.MailPit', ecosystem: 'nuget', installed: '13.6.0', locations: ['Directory.Packages.props'] },
            { name: 'astro', ecosystem: 'npm', installed: '7.4.0', locations: ['apps/careers'], current: true },
          ],
          registry: {
            'nuget:microsoft.entityframeworkcore': { distTags: {}, versions: { '10.0.14': {}, '10.0.15': {}, '11.0.0': {} }, times: { '10.0.15': '2026-11-11T00:00:00Z' } },
            'nuget:microsoft.extensions.hosting': { distTags: {}, versions: { '10.0.14': {}, '10.0.15': {}, '11.0.0': {} }, times: {} },
            'nuget:dotnet-ef': { distTags: {}, versions: { '10.0.14': {}, '10.0.15': {}, '11.0.0': {} }, times: {} },
            'nuget:aspire.hosting.postgresql': { distTags: {}, times: {}, versions: {
              '13.6.5': {}, '17.0.0': { requirements: { 'Microsoft.Extensions.Hosting': '[10.0.15, )' } } } },
            'nuget:communitytoolkit.aspire.hosting.mailpit': { distTags: {}, versions: { '13.6.0': {}, '13.6.1': {} }, times: {} },
          },
          briefs: {},
          now: Date.parse('2026-11-18T00:00:00Z'),
        })
        """;

    [Fact]
    public void A_patch_never_waits_on_a_framework_major()
    {
        var groups = Groups(Evaluate(DotNetInventory));

        Targets(groups["dotnet"]).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Microsoft.EntityFrameworkCore"] = "10.0.15",
            ["Microsoft.Extensions.Hosting"] = "10.0.15",
            ["dotnet-ef"] = "10.0.15",
        });
        groups["dotnet"].GetProperty("kind").GetString().Should().Be("patch");
        Targets(groups["dotnet-major"]).Values.Should().AllBe("11.0.0");
        groups["dotnet-major"].GetProperty("kind").GetString().Should().Be("major");
    }

    [Fact]
    public void A_major_set_shows_the_members_that_have_no_major_and_says_they_disagree()
    {
        var aspireMajor = Groups(Evaluate(DotNetInventory))["aspire-major"];

        Targets(aspireMajor).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Aspire.Hosting.PostgreSQL"] = "17.0.0",
            ["CommunityToolkit.Aspire.Hosting.MailPit"] = "13.6.1",
        });
        aspireMajor.GetProperty("targetsDisagree").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void A_NuGet_floor_shows_on_the_group_that_needs_another_group_first()
    {
        var aspireMajor = Groups(Evaluate(DotNetInventory))["aspire-major"];

        Strings(aspireMajor.GetProperty("conflicts"))
            .Should().Equal("Aspire.Hosting.PostgreSQL@17.0.0 needs Microsoft.Extensions.Hosting [10.0.15, ), planned 10.0.14");
    }

    [Fact]
    public void A_package_pnpm_outdated_did_not_list_counts_as_current_without_a_registry_read()
    {
        var inventory = Evaluate(DotNetInventory);

        Groups(inventory).Keys.Should().BeEquivalentTo("dotnet", "dotnet-major", "aspire", "aspire-major");
        inventory.GetProperty("currentCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public void A_lockstep_group_moves_as_one_and_flags_a_set_caught_mid_publish()
    {
        var groups = Groups(Evaluate("""
            m.buildInventory({
              declared: [
                { name: 'tailwindcss', ecosystem: 'npm', installed: '4.3.3', locations: ['apps/careers'] },
                { name: '@tailwindcss/vite', ecosystem: 'npm', installed: '4.3.3', locations: ['apps/careers'] },
              ],
              registry: {
                tailwindcss: { distTags: { latest: '4.3.5' }, versions: { '4.3.3': {}, '4.3.5': {} }, times: {} },
                '@tailwindcss/vite': { distTags: { latest: '4.3.4' }, versions: { '4.3.3': {}, '4.3.4': {} }, times: {} },
              },
              briefs: {}, now: 0,
            })
            """));

        groups.Keys.Should().Equal("tailwind");
        groups["tailwind"].GetProperty("members").GetArrayLength().Should().Be(2);
        groups["tailwind"].GetProperty("targetsDisagree").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void A_package_deprecated_with_nothing_to_move_to_is_reported_and_cached()
    {
        var group = Groups(Evaluate("""
            m.buildInventory({
              declared: [{ name: '@types/stub', ecosystem: 'npm', installed: '2.0.0', locations: ['apps/dashboard'] }],
              registry: { '@types/stub': { distTags: { latest: '2.0.0' }, versions: { '2.0.0': { deprecated: 'This is a stub types definition.' } }, times: {} } },
              briefs: { stub: { targets: { '@types/stub': '2.0.0' }, evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'ready' } },
              now: 0,
            })
            """))["stub"];

        group.GetProperty("kind").GetString().Should().Be("deprecated");
        group.GetProperty("research").GetString().Should().Be("reuse");
    }

    [Fact]
    public void A_blocker_on_a_package_nothing_declares_is_settled_from_its_registry_entry()
    {
        var inventory = """
            m.buildInventory({
              declared: [{ name: 'Microsoft.EntityFrameworkCore', ecosystem: 'nuget', installed: '10.0.14', locations: ['Directory.Packages.props'] }],
              registry: {
                'nuget:microsoft.entityframeworkcore': { distTags: {}, versions: { '10.0.14': {}, '11.0.0': {} }, times: {} },
                'nuget:npgsql.entityframeworkcore.postgresql': { distTags: {}, versions: { '10.0.3': {}, __NEXT__ }, times: {} },
              },
              briefs: { 'microsoft.entityframeworkcore-major': { targets: { 'Microsoft.EntityFrameworkCore': '11.0.0' },
                evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'blocked',
                blocker: { blockedOn: { 'nuget:Npgsql.EntityFrameworkCore.PostgreSQL': '10.0.3' } } } },
              now: 0,
            })
            """;

        Groups(Evaluate(inventory.Replace("__NEXT__", "")))["microsoft.entityframeworkcore-major"]
            .GetProperty("research").GetString().Should().Be("blocked");
        Groups(Evaluate(inventory.Replace("__NEXT__", "'11.0.0': {}")))["microsoft.entityframeworkcore-major"]
            .GetProperty("research").GetString().Should().Be("recheck");
    }

    [Fact]
    public void A_filed_task_stays_attached_when_a_new_release_moves_the_target()
    {
        var group = Groups(Evaluate("""
            m.buildInventory({
              declared: [{ name: 'vite', ecosystem: 'npm', installed: '8.3.4', locations: ['apps/dashboard'] }],
              registry: { vite: { distTags: { latest: '8.3.9' }, versions: { '8.3.4': {}, '8.3.8': {}, '8.3.9': {} }, times: {} } },
              briefs: { vite: { targets: { vite: '8.3.8' }, evaluatedAt: '2026-01-01T00:00:00.000Z', verdict: 'ready', task: { displayId: 12 } } },
              now: 0,
            })
            """))["vite"];

        group.GetProperty("research").GetString().Should().Be("delta");
        group.GetProperty("task").GetProperty("displayId").GetInt32().Should().Be(12);
    }

    [Fact]
    public void An_advisory_against_the_package_itself_is_told_apart_from_one_inherited_from_below()
    {
        var audit = Evaluate("""
            m.splitPnpmAudit({ advisories: {
              1: { severity: 'high', findings: [{ paths: ['apps\\dashboard > @sveltejs/kit@3.1.0'] }] },
              2: { severity: 'moderate', findings: [{ paths: ['packages\\api-client > orval@8.43.0 > undici@7.29.1'] }] },
              3: { severity: 'low', findings: [{ paths: ['apps\\dashboard > vite@8.3.4 > esbuild@0.28.2'] }] },
              4: { severity: 'critical', findings: [{ paths: ['packages\\api-client > orval@8.43.0 > a@1.0.0 > b@2.0.0'] }] },
            } })
            """);

        audit.GetProperty("advisories").GetProperty("@sveltejs/kit").GetString().Should().Be("high");
        audit.GetProperty("inheritedAdvisories").GetProperty("orval").GetString().Should().Be("critical");
        audit.GetProperty("inheritedAdvisories").GetProperty("vite").GetString().Should().Be("low");
        audit.GetProperty("transitiveCount").GetInt32().Should().Be(3);
    }

    private const string Lockfile = """
        lockfileVersion: '9.0'

        importers:

          .: {}

          apps/careers:
            dependencies:
              '@astrojs/node':
                specifier: ^11.1.8
                version: 11.1.8(astro@7.4.0(@types/node@24.19.2))
              '@screenhire/ui':
                specifier: workspace:*
                version: link:../../packages/ui
            devDependencies:
              typescript:
                specifier: ~6.0.3
                version: 6.0.3

          apps/dashboard:
            devDependencies:
              '@tiptap/core':
                specifier: 3.32.0
                version: 3.32.0
              typescript:
                specifier: ~6.0.3
                version: 6.0.2

        packages:

          '@astrojs/node@11.1.8':
            resolution: {integrity: sha512-x}
            peerDependencies:
              astro: ^7.0.0
              '@types/node': '>=18'

          ms@2.1.3:
            resolution: {integrity: sha512-y}

        snapshots:

          ms@2.1.3: {}
        """;

    [Fact]
    public void The_lockfile_reader_takes_direct_dependencies_and_peer_ranges()
    {
        var lockfile = Evaluate($"m.readPnpmLockfile({JsonSerializer.Serialize(Lockfile)})");

        var careers = lockfile.GetProperty("importers").GetProperty("apps/careers");
        careers.GetProperty("@astrojs/node").GetProperty("version").GetString().Should().Be("11.1.8");
        careers.GetProperty("typescript").GetProperty("block").GetString().Should().Be("devDependencies");
        lockfile.GetProperty("importers").GetProperty(".").EnumerateObject().Should().BeEmpty();
        var peers = lockfile.GetProperty("packages").GetProperty("@astrojs/node@11.1.8").GetProperty("peerDependencies");
        peers.GetProperty("@types/node").GetString().Should().Be(">=18");
        lockfile.GetProperty("packages").EnumerateObject().Select(property => property.Name).Should().Equal("@astrojs/node@11.1.8", "ms@2.1.3");
    }

    [Fact]
    public void Declared_npm_packages_skip_workspace_links_mark_exact_pins_and_report_the_lowest_copy()
    {
        var declared = Evaluate($$"""
            m.collectNpmDeclared([{ location: '.', lockfile: m.readPnpmLockfile({{JsonSerializer.Serialize(Lockfile)}}) },
              { location: 'tools/lighthouse', lockfile: { importers: { '.': { lighthouse: { specifier: '^13.5.0', version: '13.6.0' } } }, packages: {} } }])
            """).EnumerateArray().ToDictionary(record => record.GetProperty("name").GetString()!);

        declared.Keys.Should().BeEquivalentTo("@astrojs/node", "typescript", "@tiptap/core", "lighthouse");
        declared["typescript"].GetProperty("installed").GetString().Should().Be("6.0.2");
        Strings(declared["typescript"].GetProperty("locations")).Should().Equal("apps/careers", "apps/dashboard");
        declared["typescript"].GetProperty("pinned").GetBoolean().Should().BeFalse();
        declared["@tiptap/core"].GetProperty("pinned").GetBoolean().Should().BeTrue();
        Strings(declared["lighthouse"].GetProperty("locations")).Should().Equal("tools/lighthouse");
    }

    private const string PackagesProps = """
        <Project>
          <!-- A comment that names <PackageVersion Include="Ignored" Version="1.0.0" /> is not a pin. -->
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
            <AspireVersion>13.6.5</AspireVersion>
          </PropertyGroup>
          <ItemGroup Label="Aspire">
            <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="$(AspireVersion)" />
            <PackageVersion Include="CommunityToolkit.Aspire.Hosting.MailPit" Version="13.6.0" />
          </ItemGroup>
          <ItemGroup Label="Transitive pins">
            <PackageVersion Include="MessagePack" Version="2.5.307" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void Directory_Packages_props_resolves_property_versions_and_keeps_labels()
    {
        var tools = JsonSerializer.Serialize("""{ "tools": { "aspire.cli": { "version": "13.6.5", "commands": ["aspire"] } } }""");
        var declared = Evaluate($$"""
            m.collectNuGetDeclared(m.readPackagesProps({{JsonSerializer.Serialize(PackagesProps)}}), m.readToolsManifest({{tools}}))
            """).EnumerateArray().ToDictionary(record => record.GetProperty("name").GetString()!);

        declared.Keys.Should().BeEquivalentTo("Aspire.Hosting.PostgreSQL", "CommunityToolkit.Aspire.Hosting.MailPit", "MessagePack", "aspire.cli");
        declared["Aspire.Hosting.PostgreSQL"].GetProperty("installed").GetString().Should().Be("13.6.5");
        declared["Aspire.Hosting.PostgreSQL"].GetProperty("property").GetString().Should().Be("AspireVersion");
        declared["MessagePack"].GetProperty("transitivePin").GetBoolean().Should().BeTrue();
        declared["aspire.cli"].GetProperty("tool").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void The_readers_understand_the_repos_own_lockfile_and_central_packages()
    {
        // A format change in pnpm or in Directory.Packages.props must fail here, not in an unattended run.
        var lockfile = JsonSerializer.Serialize(File.ReadAllText(RepositoryFiles.PathTo("pnpm-lock.yaml")));
        var props = JsonSerializer.Serialize(File.ReadAllText(RepositoryFiles.PathTo("Directory.Packages.props")));

        var npm = Evaluate($"m.collectNpmDeclared([{{ location: '.', lockfile: m.readPnpmLockfile({lockfile}) }}]).map(({{ name }}) => name)");
        Strings(npm).Should().Contain(["astro", "svelte", "typescript", "vitest"]);
        var nuget = Evaluate($"m.readPackagesProps({props}).packages.filter(({{ property }}) => property).map(({{ name, version }}) => m.parseVersion(version) !== null)");
        nuget.EnumerateArray().Should().NotBeEmpty().And.OnlyContain(valid => valid.GetBoolean());
    }

    private const string Before = """
        { pnpm: { '.': { importers: { 'apps/dashboard': { vite: { version: '8.3.4' }, svelte: { version: '5.57.2' } }, 'packages/realtime': { vite: { version: '8.3.4' } } },
                         packages: { 'vite@8.3.4': {}, 'svelte@5.57.2': {}, 'rollup@4.1.0': {} } } },
          nuget: { 'microsoft.entityframeworkcore': { name: 'Microsoft.EntityFrameworkCore', version: '10.0.14', where: 'Directory.Packages.props' } } }
        """;

    [Fact]
    public void The_diff_reports_what_a_phase_moved_and_the_check_proves_every_copy_moved()
    {
        var after = """
            { pnpm: { '.': { importers: { 'apps/dashboard': { vite: { version: '8.3.8' }, svelte: { version: '5.57.2' } }, 'packages/realtime': { vite: { version: '8.3.8' } } },
                             packages: { 'vite@8.3.8': {}, 'svelte@5.57.2': {}, 'rollup@4.2.0': {}, 'fdir@6.0.0': {} } } },
              nuget: { 'microsoft.entityframeworkcore': { name: 'Microsoft.EntityFrameworkCore', version: '10.0.15', where: 'Directory.Packages.props' } } }
            """;

        var diff = Evaluate($"m.diffDependencies({Before}, {after})");
        Strings(diff.GetProperty("changed")).Should().BeEquivalentTo("vite 8.3.4 -> 8.3.8", "rollup 4.1.0 -> 4.2.0");
        Strings(diff.GetProperty("added")).Should().Equal("fdir@6.0.0");
        diff.GetProperty("directMoves").GetArrayLength().Should().Be(3);

        var checks = Evaluate($"m.checkExpected(['vite@8.3.8', 'microsoft.entityframeworkcore@10.0.15', 'zod@4.0.0'], {after}).map(({{ ok }}) => ok)");
        checks.EnumerateArray().Select(check => check.GetBoolean()).Should().Equal(true, true, false);
    }

    [Fact]
    public void The_check_fails_a_package_one_workspace_copy_of_which_stayed_behind()
    {
        var after = """
            { pnpm: { '.': { importers: { 'apps/dashboard': { vite: { version: '8.3.8' } }, 'packages/realtime': { vite: { version: '8.3.4' } } }, packages: {} } }, nuget: {} }
            """;

        Evaluate($"m.checkExpected(['vite@8.3.8'], {after})[0].ok").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Scoped_names_split_at_their_last_at_sign()
    {
        Strings(Evaluate("m.splitSpec('@sveltejs/kit@3.3.1')")).Should().Equal("@sveltejs/kit", "3.3.1");
        Strings(Evaluate("m.splitSpec('nuget:Aspire.Hosting@17.0.0')")).Should().Equal("nuget:Aspire.Hosting", "17.0.0");
    }

    private static Dictionary<string, JsonElement> Groups(JsonElement inventory) =>
        inventory.GetProperty("groups").EnumerateArray().ToDictionary(group => group.GetProperty("group").GetString()!);

    private static Dictionary<string, string> Targets(JsonElement group) =>
        group.GetProperty("members").EnumerateArray()
            .ToDictionary(member => member.GetProperty("name").GetString()!, member => member.GetProperty("target").GetString()!);

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    /// <summary>
    /// Imports the script by file URL (a bare Windows path is not a valid specifier) as <c>m</c>, evaluates
    /// <paramref name="expression"/> and returns its JSON. Run through stdin, so <c>process.argv[1]</c> is unset and the
    /// script's command line stays off.
    /// </summary>
    private static JsonElement Evaluate(string expression)
    {
        var driver = $$"""
            import { pathToFileURL } from 'node:url';
            const m = await import(pathToFileURL(process.env.UPGRADE_DEPS_SCRIPT).href);
            const result = await (
            {{expression}}
            );
            console.log(JSON.stringify(result === undefined ? null : result));
            """;
        var start = new ProcessStartInfo("node", ["--input-type=module"])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        start.Environment["UPGRADE_DEPS_SCRIPT"] = RepositoryFiles.PathTo("scripts", "upgrade-deps.mjs");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        process.StandardInput.Write(driver);
        process.StandardInput.Close();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, errorTask.Result);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }
}
