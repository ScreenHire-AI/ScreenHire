using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>The marketing site is static HTML: content without JavaScript (CLAUDE.md: public pages ship ~0 KB JS).</summary>
public sealed class MarketingTests(E2EFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task Home_page_has_a_heading_and_ships_no_javascript()
    {
        var scriptRequests = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.ResourceType == "script")
            {
                scriptRequests.Add(request.Url);
            }
        };

        await GoToAsync("marketing", "/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Know who's really applying.");
        var scriptTypes = await Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.type)");
        scriptTypes.Should().NotBeEmpty("the page carries JSON-LD").And.OnlyContain(type => type == "application/ld+json");
        scriptRequests.Should().BeEmpty();

        foreach (var jsonLd in await Page.Locator("script[type='application/ld+json']").AllTextContentsAsync())
        {
            using var document = JsonDocument.Parse(jsonLd);
            document.RootElement.GetProperty("@context").GetString().Should().Be("https://schema.org");
        }

        await ExpectNoAccessibilityViolationsAsync();
    }
}
