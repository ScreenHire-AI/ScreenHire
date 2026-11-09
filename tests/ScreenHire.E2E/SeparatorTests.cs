namespace ScreenHire.E2E;

/// <summary>
/// The shared separator (<c>ui/separator</c>) takes its size from bits-ui's <c>data-orientation</c>: a horizontal one is a
/// hairline 1px high and as wide as its box, a vertical one 1px wide. It once keyed on attributes bits-ui never sets, so
/// every separator drew nothing and the sidebar worked around it.
/// </summary>
public sealed class SeparatorTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Fact]
    public async Task Separators_are_a_hairline_inside_their_box()
    {
        await PrepareAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync();

        // The sidebar's hairline above the person's block spans the sidebar less its 8px margins.
        var sidebar = await Page.Locator("[data-slot=sidebar-separator]").EvaluateAsync<double[]>(
            "el => { const r = el.getBoundingClientRect(); const p = el.parentElement.getBoundingClientRect(); return [r.height, r.width, p.width]; }");
        sidebar[0].Should().Be(1, "a horizontal separator is 1px high");
        sidebar[1].Should().Be(sidebar[2] - 16, "the sidebar's separator fills its box between the margins of mx-2");

        // The date range's menu has one between its presets and its custom range.
        await Page.GotoAsync(DashboardUrl("/reports"));
        await SettleAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Dates:") }).ClickAsync();
        var inMenu = Page.Locator("[data-slot=separator]");
        await Expect(inMenu.First).ToBeVisibleAsync();
        (await inMenu.First.BoundingBoxAsync())!.Height.Should().Be(1);
    }
}
