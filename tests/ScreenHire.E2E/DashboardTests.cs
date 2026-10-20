using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>The recruiter dashboard SPA, rendered in a real browser against the API (seeded Brightwater data).</summary>
public sealed partial class DashboardTests(E2EFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task Jobs_table_shows_every_job_with_its_status()
    {
        await GoToAsync("dashboard", "/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
        var table = Page.GetByRole(AriaRole.Region, new() { Name = "Jobs" }).GetByRole(AriaRole.Table);
        var rows = table.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Cell) });
        await Expect(rows).ToHaveCountAsync(4);

        (string Title, string Location, string Status)[] seeded =
        [
            ("Senior Product Designer", "Remote — UK & EU", "Open"),
            ("Senior .NET Engineer", "Remote — UK & EU", "Open"),
            ("Frontend Engineer (Svelte)", "London, UK", "Open"),
            ("Customer Success Lead", "Manchester, UK", "Draft"),
        ];
        foreach (var (title, location, status) in seeded)
        {
            var cells = rows.Filter(new() { HasText = title }).GetByRole(AriaRole.Cell);
            await Expect(cells.Nth(0)).ToHaveTextAsync(title);
            await Expect(cells.Nth(1)).ToHaveTextAsync(location);
            await Expect(cells.Nth(2)).ToHaveTextAsync(status);
            // Published date ("20 Oct") for open jobs; a dash for the draft, which was never published.
            await Expect(cells.Nth(3)).ToHaveTextAsync(status == "Draft" ? new Regex("^—$") : PublishedDate());
        }

        await Expect(Page.GetByText("4 jobs · 3 open")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
    }

    [GeneratedRegex(@"^\d{1,2} \w{3}$")]
    private static partial Regex PublishedDate();
}
