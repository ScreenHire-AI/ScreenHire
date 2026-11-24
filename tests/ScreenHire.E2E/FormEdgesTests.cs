namespace ScreenHire.E2E;

/// <summary>
/// The edge of a text field on the careers apply page is at least 3:1 against the page and the card it sits on (WCAG
/// 1.4.11), in the light theme the page uses and in the dark tokens that exist for it (<c>--candidate-*</c> under
/// <c>.dark</c>), which a class on the html element turns on. The dashboard's forms are measured by the dark and phone walks,
/// but for an open question's, which the walks never open: they are here, with the rule that nothing has one thick coloured
/// side (design system S11), measured as the browser drew it.
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

    /// <summary>
    /// Everything on the page with one side's border thicker than the others by more than a hairline, in a colour, and every
    /// bar drawn down a side (a positioned box or pseudo-element up to 6px wide and as tall as a box over 24px it sits in).
    /// </summary>
    private const string ThickSidesScript = """
        () => {
          const found = [];
          const seen = (colour) => colour && colour !== 'transparent' && !/, 0\)$|\/ 0\)$/.test(colour);
          const name = (element) => `${element.tagName.toLowerCase()}[${[...element.attributes].filter((a) => a.name.startsWith('data-')).map((a) => a.name).join(' ')}] ${(element.textContent ?? '').trim().slice(0, 30)}`;
          for (const element of document.querySelectorAll('main *')) {
            const box = element.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) continue;
            const style = getComputedStyle(element);
            for (const [side, opposite] of [['Left', 'Right'], ['Right', 'Left']]) {
              if (parseFloat(style[`border${side}Width`]) - parseFloat(style[`border${opposite}Width`]) > 1 && seen(style[`border${side}Color`])) found.push(`${name(element)}: a thick ${side.toLowerCase()} border`);
            }
            for (const pseudo of ['::before', '::after']) {
              const drawn = getComputedStyle(element, pseudo);
              if (drawn.content !== 'none' && drawn.position === 'absolute' && box.height > 24 && parseFloat(drawn.width) > 1 && parseFloat(drawn.width) <= 6 && parseFloat(drawn.height) >= box.height - 4 && seen(drawn.backgroundColor)) found.push(`${name(element)}: a bar in its ${pseudo}`);
            }
            const parent = element.offsetParent?.getBoundingClientRect();
            if (style.position === 'absolute' && parent && parent.height > 24 && box.width > 1 && box.width <= 6 && box.height >= parent.height - 4 && seen(style.backgroundColor)) found.push(`${name(element)}: a bar down its side`);
          }
          return found;
        }
        """;

    [Fact]
    public async Task An_open_question_and_the_verification_summary_have_no_thick_side_and_their_fields_an_edge_of_3_to_1()
    {
        await PrepareAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync();

        await Page.GotoAsync(DashboardUrl($"/templates/{Scene.TemplateId}?tab=forms"));
        var questions = Page.GetByRole(AriaRole.Region, new() { Name = "Questions", Exact = true });
        await questions.GetByRole(AriaRole.Button, new() { Name = "Add a question" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Yes or no", Exact = true }).ClickAsync();
        await Expect(questions.Locator("[data-open] [data-question-body]").First).ToBeVisibleAsync();
        await CollectFieldEdgesAsync("an open question");
        await CollectThickSidesAsync("the Forms tab with a question open");

        await Page.Locator("[data-open] [data-question-header]").First.EvaluateAsync("strip => { strip.style.borderLeft = '4px solid rebeccapurple'; }");
        (await Page.EvaluateAsync<string[]>(ThickSidesScript)).Should().NotBeEmpty("a control: a thick side is reported, so the pass means it was measured");

        // A question was added and never saved, so leaving the template asks first.
        Page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
        await Page.GotoAsync(DashboardUrl($"/applications/{Scene.BotApplicationId}?tab=verification"));
        var summary = Page.Locator("[data-summary]");
        await Expect(summary).ToBeVisibleAsync();
        await CollectThickSidesAsync("the Verification tab");
        var edges = await summary.EvaluateAsync<string[]>("el => { const s = getComputedStyle(el); return [s.borderTopWidth, s.borderRightWidth, s.borderBottomWidth, s.borderLeftWidth]; }");
        edges.Distinct().Should().ContainSingle("the summary has its full border").Which.Should().Be("1px");
        foreach (var (width, height) in new[] { (390, 844), (1440, 1000), (2560, 1300) })
        {
            await Page.SetViewportSizeAsync(width, height);
            await CollectOverflowAsync($"the Verification tab at {width}px");
            await SaveScreenshotAsync(Page, $"dashboard-verification-summary-{width}");
        }

        Problems.Should().BeEmpty("no thick side and no faint field ({0})", string.Join("; ", Problems));
    }

    private async Task CollectThickSidesAsync(string name)
    {
        foreach (var thick in await Page.EvaluateAsync<string[]>(ThickSidesScript))
        {
            Problems.Add($"{name}: {thick}");
        }
    }
}
