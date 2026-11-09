namespace ScreenHire.E2E;

/// <summary>
/// The edge of a text field on the careers apply page is at least 3:1 against the page and the card it sits on (WCAG
/// 1.4.11), in the light theme the page uses and in the dark tokens that exist for it (<c>--candidate-*</c> under
/// <c>.dark</c>), which a class on the html element turns on. The dashboard's forms are measured by the dark and phone walks.
/// </summary>
public sealed class FormEdgesTests(E2EFixture fixture) : ProofWalk(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_apply_pages_text_fields_have_an_edge_of_3_to_1(bool dark)
    {
        await PrepareAsync();
        await Page.SetViewportSizeAsync(1280, 900);
        await GoToAsync("careers", $"/{Scene.Slug}");
        await Page.GetByRole(AriaRole.Link, new() { Name = ProofScene.JobTitle }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Apply" }).First.ClickAsync();
        await Expect(Page.Locator("input[type=email]").First).ToBeVisibleAsync();
        await Page.Locator("astro-island:not([ssr])").WaitForAsync();

        if (dark)
        {
            await Page.EvaluateAsync("() => document.documentElement.classList.add('dark')");
        }

        var theme = dark ? "dark" : "light";
        await CollectFieldEdgesAsync($"the apply page ({theme})");
        _ = await ScreenshotAsync($"careers-{theme}", "apply");
        Problems.Should().BeEmpty("every text field's edge reads at 3:1 ({0})", string.Join("; ", Problems));

        if (!dark)
        {
            // A control for the check, in the light theme (a light edge on the dark page would read well): the same fields with a faint edge must be reported, so the pass above means it measured them.
            // Through the style object: the careers pages' content security policy refuses a style tag.
            await Page.EvaluateAsync("() => document.querySelectorAll('input, textarea, select').forEach((field) => field.style.setProperty('border-color', 'rgb(230, 225, 216)', 'important'))");
            await CollectFieldEdgesAsync("the apply page with a faint edge");
            Problems.Should().NotBeEmpty("a 1.3:1 edge is reported");
        }
    }
}
