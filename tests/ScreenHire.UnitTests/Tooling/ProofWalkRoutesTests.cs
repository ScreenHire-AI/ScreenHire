using System.Text.RegularExpressions;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The proof walks open each Verification page from <c>ProofWalk</c>'s route table and do not click the area's tabs
/// (<c>ProofWalk.TabsAreRoutes</c>): the tabs are the pages, and clicking every tab from every page checked each page
/// eight times. That holds only while every tab is a row of the table. A tab added to
/// <c>apps/dashboard/src/lib/verification-area.ts</c> with no row would be a page neither walk opens, in the dark theme
/// or on a phone, so it fails here. Each red case edits a real file in memory, so a collector that stops matching fails.
/// </summary>
public sealed partial class ProofWalkRoutesTests
{
    private const string TabsPath = "apps/dashboard/src/lib/verification-area.ts";

    private const string WalkPath = "tests/ScreenHire.E2E/ProofWalk.cs";

    [Fact]
    public void Every_tab_of_the_verification_area_is_a_route_the_walk_opens()
    {
        TabsTheWalkSkips(Read(TabsPath), Read(WalkPath)).Should().BeEmpty();
    }

    [Fact]
    public void The_check_reads_the_real_files()
    {
        TabAddresses(Read(TabsPath)).Should().Contain(["/verification", "/verification/activity", "/verification/checks"])
            .And.HaveCountGreaterThanOrEqualTo(3, "the area has three tabs");
        WalkedRoutes(Read(WalkPath)).Should().Contain(["/", "/candidates", "/verification", "/settings/team"])
            .And.HaveCountGreaterThan(30, "the route table holds every dashboard page");
    }

    [Theory]
    // A new tab with no row in the walk's table.
    [InlineData(TabsPath, "href: '/verification/checks'", "href: '/verification/spend'", "/verification/spend")]
    // A row taken out of the table while its tab stays.
    [InlineData(WalkPath, "        (\"verification\", _ => \"/verification/checks\"),\n", "", "/verification/checks")]
    public void A_tab_the_walk_would_not_open_is_reported(string path, string needle, string replacement, string expected)
    {
        var files = new Dictionary<string, string> { [TabsPath] = Read(TabsPath), [WalkPath] = Read(WalkPath) };
        files[path].Should().Contain(needle, "a stale needle would leave the file as it is and prove nothing");
        files[path] = files[path].Replace(needle, replacement, StringComparison.Ordinal);

        TabsTheWalkSkips(files[TabsPath], files[WalkPath]).Should().Equal(expected);
    }

    /// <summary>The Verification tabs' addresses that are not a route of the walk's table.</summary>
    private static List<string> TabsTheWalkSkips(string tabs, string walk) => [.. TabAddresses(tabs).Except(WalkedRoutes(walk))];

    private static List<string> TabAddresses(string tabs) => [.. TabAddress().Matches(tabs).Select(match => match.Groups["address"].Value)];

    private static HashSet<string> WalkedRoutes(string walk) =>
        RouteRow().Matches(walk).Select(match => match.Groups["path"].Value).ToHashSet(StringComparer.Ordinal);

    private static string Read(string path) => File.ReadAllText(RepositoryFiles.PathTo(path.Split('/'))).ReplaceLineEndings("\n");

    // A tab's row in `verificationTabs`: `href: '/verification/activity'`.
    [GeneratedRegex(@"\bhref:\s*'(?<address>/verification[^']*)'")]
    private static partial Regex TabAddress();

    // A row of the route table, whose path is a lambda over the scene: `("work", _ => "/candidates")`. The table of a
    // new organization's pages holds plain strings and is not read: its walk clicks no Verification page's tabs either,
    // and it lists three of the seven on purpose.
    [GeneratedRegex(@"=>\s*\$?""(?<path>/[^""]*)""")]
    private static partial Regex RouteRow();
}
