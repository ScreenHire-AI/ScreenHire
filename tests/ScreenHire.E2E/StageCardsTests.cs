using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// The Pipeline and Forms tabs where a pipeline is edited (design system section 11: Stage cards, Stage chips, Form
/// cards; the owner's walk-through, 2026-11-22). The cards share the panel's width in CSS from the container's width and
/// the number of stages, which jsdom cannot lay out, so the shares, the stacking on a phone and the absence of a sideways
/// scroll are checked here in a real browser, with a picture at each width for the design review. So is what shows
/// before anything is hovered, and where the focus goes when a stage is removed from its sheet.
/// </summary>
public sealed class StageCardsTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1440, 1000, true)]
    [InlineData(2560, 1300, true)]
    [InlineData(390, 844, false)]
    public async Task The_stage_cards_share_the_panels_width_in_a_row_and_stack_on_a_phone_and_the_form_cards_never_scroll_the_page(int width, int height, bool inARow)
    {
        await Page.SetViewportSizeAsync(width, height);
        var email = await NewMemberAsync();
        var jobId = await NewJobAsync(email);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}/workflow"));

        var stages = Page.GetByRole(AriaRole.Region, new() { Name = "Stages" });
        var cards = stages.GetByRole(AriaRole.Group, new() { NameRegex = new("^Stage \\d+: ") });
        await Expect(cards.First).ToBeVisibleAsync();
        var boxes = await cards.EvaluateAllAsync<double[][]>("cards => cards.map(card => { const box = card.getBoundingClientRect(); return [box.x, box.y, box.width]; })");
        var row = await stages.Locator(".stage-row").EvaluateAsync<double>("row => row.getBoundingClientRect().width");
        boxes.Length.Should().BeGreaterThan(2, "a new job's pipeline has several open stages");

        if (inARow)
        {
            // One row of equal shares that fills the panel: nothing wraps and no half of it sits empty.
            boxes.Select(box => Math.Round(box[1])).Distinct().Should().ContainSingle("every stage shares one row at {0}px", width);
            boxes.Select(box => box[0]).Should().BeInAscendingOrder();
            (boxes.Max(box => box[2]) - boxes.Min(box => box[2])).Should().BeLessThan(2, "the stages share the row equally");
            var used = boxes[^1][0] + boxes[^1][2] - boxes[0][0];
            used.Should().BeGreaterThan(row * 0.9, "the cards fill the panel's width at {0}px", width);
        }
        else
        {
            boxes.Select(box => Math.Round(box[0])).Distinct().Should().ContainSingle("the stages stack at {0}px", width);
            boxes.Select(box => box[1]).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        }

        // Each card counts what happens there and reads no sentence; a stage with nothing on it says so.
        (await cards.EvaluateAllAsync<bool>("cards => cards.every(card => card.querySelector('ul[aria-label^=\"What happens at\"], [data-chips-empty]') !== null)"))
            .Should().BeTrue("every card has its chips or says nothing happens there");

        // The grip and the plus show with the pointer away (F4). Playwright counts an opacity of 0 as visible, so the page is asked,
        // and it looks at what is around them too.
        await Page.Mouse.MoveAsync(0, 0);
        (await cards.EvaluateAllAsync<bool>(
            "cards => cards.every(card => ['[data-reorder-grip]', '[data-stage-add]'].every(part => card.querySelector(part)?.checkVisibility({ opacityProperty: true, visibilityProperty: true })))"))
            .Should().BeTrue("the grip and the plus of every card show without hovering");
        (await stages.Locator("[data-stage-header]").EvaluateAllAsync<bool>(
            "headers => headers.every(header => { const tops = [...header.children].map(part => part.getBoundingClientRect()); return tops.every(box => box.top < tops[0].bottom) && tops.every((box, at) => at === 0 || box.left > tops[at - 1].left); })"))
            .Should().BeTrue("the grip, the number and the name are one row, in that order");
        await Expect(cards.Filter(new() { HasText = "Interview" }).GetByRole(AriaRole.Button, new() { Name = "Scorecard" })).ToHaveTextAsync("Scorecard", new() { UseInnerText = true });
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the Pipeline tab at {0}px scrolls sideways", width);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, $"dashboard-pipeline-stage-cards-{width}");

        await Page.GetByRole(AriaRole.Tab, new() { Name = "Forms" }).ClickAsync();
        var forms = Page.GetByRole(AriaRole.Group, new() { Name = "Forms", Exact = true });
        var application = forms.GetByRole(AriaRole.Button, new() { NameRegex = new("^Application form") });
        await Expect(application).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(application).ToContainTextAsync("When they apply");
        await Expect(forms.GetByRole(AriaRole.Button, new() { Name = "New form" })).ToBeVisibleAsync();
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the Forms tab at {0}px scrolls sideways", width);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, $"dashboard-pipeline-form-cards-{width}");
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1024)]
    public async Task On_touch_the_grip_the_name_and_the_plus_are_44px_targets_that_cover_no_chip(int width)
    {
        await UseTouchScreenAsync();
        // Tall, so the sticky Save bar hides nothing and the picture shows the Ends row.
        await Page.SetViewportSizeAsync(width, 1600);
        var email = await NewMemberAsync();
        var jobId = await NewJobAsync(email);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}/workflow"));
        var stages = Page.GetByRole(AriaRole.Region, new() { Name = "Stages" });
        await Expect(stages.GetByRole(AriaRole.Region, new() { Name = "Ends" })).ToBeVisibleAsync();

        (await Page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches")).Should().BeTrue("the page is measured as a touch screen");
        var small = await stages.Locator("[data-reorder-grip], [data-stage-card] [data-stage-open], [data-stage-add]").EvaluateAllAsync<string[]>(
            "parts => parts.map(part => [part.getAttribute('aria-label') ?? part.textContent.trim(), part.getBoundingClientRect()]).filter(([, box]) => box.width < 43.5 || box.height < 43.5).map(([name, box]) => `${name} ${box.width}x${box.height}`)");
        small.Should().BeEmpty("every control of a card is 44px on touch (F4)");
        var covered = await stages.EvaluateAsync<string[]>(
            """
            region => {
              const meet = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 0.5 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 0.5;
              const chips = [...region.querySelectorAll('[data-chip], [data-end-stage]')];
              return [...region.querySelectorAll('[data-stage-add]')].flatMap(plus => chips
                .filter(chip => meet(plus.getBoundingClientRect(), chip.getBoundingClientRect()))
                .map(chip => `${plus.getAttribute('aria-label')} over ${chip.textContent.trim()}`));
            }
            """);
        covered.Should().BeEmpty("the plus takes no tap meant for a chip");
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue("the Pipeline tab at {0}px on touch scrolls sideways", width);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, $"dashboard-pipeline-stage-cards-touch-{width}");

        await stages.GetByRole(AriaRole.Button, new() { Name = "Screening", Exact = true }).TapAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Screening" }).GetByRole(AriaRole.Button, new() { Name = "Done" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, $"dashboard-pipeline-stage-sheet-touch-{width}");
        if (width == 390)
            (await Page.GetByRole(AriaRole.Dialog, new() { Name = "Screening" }).BoundingBoxAsync())!.X.Should().BeLessThan(1, "the sheet has the whole width of a phone");
    }

    [Fact]
    public async Task Removing_a_stage_from_its_sheet_puts_the_focus_on_the_card_that_took_its_place()
    {
        await Page.SetViewportSizeAsync(1440, 1000);
        var email = await NewMemberAsync();
        var jobId = await NewJobAsync(email);
        await SignInAsync(email);
        await Page.GotoAsync(DashboardUrl($"/jobs/{jobId}/workflow"));
        var stages = Page.GetByRole(AriaRole.Region, new() { Name = "Stages" });
        await Expect(stages.GetByRole(AriaRole.Button, new() { NameRegex = new("^(Actions|More) for ") })).ToHaveCountAsync(0);

        await stages.GetByRole(AriaRole.Button, new() { Name = "Screening", Exact = true }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Screening" });
        await Expect(sheet.GetByRole(AriaRole.Heading, new() { Level = 3 })).ToHaveTextAsync(["Timing", "Meeting", "Scorecard", "Automations"]);
        // The menu at the left, Done at the right at its normal width.
        var menu = (await sheet.GetByRole(AriaRole.Button, new() { Name = "More for Screening" }).BoundingBoxAsync())!;
        var done = (await sheet.GetByRole(AriaRole.Button, new() { Name = "Done" }).BoundingBoxAsync())!;
        done.X.Should().BeGreaterThan(menu.X + menu.Width);
        done.Width.Should().BeLessThan(120);
        await SaveScreenshotAsync(Page, "dashboard-pipeline-stage-sheet-1440");

        await sheet.GetByRole(AriaRole.Button, new() { Name = "More for Screening" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove stage" }).ClickAsync();

        await Expect(sheet).ToBeHiddenAsync();
        await Expect(stages.GetByRole(AriaRole.Group, new() { NameRegex = new("^Stage \\d+: Screening$") })).ToHaveCountAsync(0);
        await Expect(stages.GetByRole(AriaRole.Button, new() { Name = "Interview", Exact = true })).ToBeFocusedAsync();
    }

    private async Task<Guid> NewJobAsync(string email)
    {
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using var response = await session.PostAsync("/v1/jobs", new { title = "Service plumber" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<string> NewMemberAsync()
    {
        var email = ApiSession.NewEmail("e2e-stage-cards");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using var onboarded = await session.PostAsync("/v1/auth/onboarding", new { name = "Riley Harper", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct);
        onboarded.StatusCode.Should().Be(HttpStatusCode.OK);
        return email;
    }

    private async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
