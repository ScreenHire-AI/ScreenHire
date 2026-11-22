using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// Settings > Billing in a real browser, against the local sandbox payment provider and the plans the catalogue ships
/// (docs/architecture/billing.md, ADR-0029, read through <see cref="ShippedPlans"/>): Starter, Growth (the trial's plan)
/// and Unlimited. An Owner sees the free trial and the three plans, chooses one through the sandbox's checkout and comes
/// back subscribed, with the month added up, the card and the sandbox's invoices, and reaches Billing from their own menu; an Owner
/// buys AI credits through the sandbox, sees the balance and the purchase, and turns auto-reload on (ADR-0032); a
/// publish past the plan's limit is refused with a notice that links to Billing; a Recruiter cannot open the page and has no
/// Billing in their menu. Each test signs in as a new person with a new organization, which starts on its own 14-day trial.
/// </summary>
public sealed class BillingTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Description =
        "<p>Shape how small hiring teams review applicants: research, prototype and ship the recruiter dashboard with engineering, end to end.</p>";

    private static readonly string[] UkAndIreland = ["GB", "IE"];

    private static CancellationToken Ct =>TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_sees_the_trial_and_the_plans_and_chooses_one_through_checkout()
    {
        var owner = ApiSession.NewEmail("e2e-billing-owner");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);

        // While the trial runs, the Owner's sidebar says when it ends, in one quiet line above their own block, and links to Billing.
        var trialEnds = Page.GetByRole(AriaRole.Link, new() { NameRegex = new("^Trial ends ") });
        await Expect(trialEnds).ToBeVisibleAsync();
        await Expect(trialEnds).ToHaveAttributeAsync("href", "/settings/billing");

        // Billing is in the Settings menu under Organization, for an owner.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true }).GetByRole(AriaRole.List, new() { Name = "Organization", Exact = true })
            .GetByRole(AriaRole.Link, new() { Name = "Billing" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/billing"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Billing");

        // A new organization is on the free trial, on the trial plan, with nothing open yet. The plan card is the trial card: the
        // days left (14 calendar days, give or take one across a clock change), the trial's plan, its meter and Choose a plan.
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Free trial");
        var plan = Page.GetByRole(AriaRole.Region, new() { Name = "Plan", Exact = true });
        await Expect(plan.Locator("[data-plan-headline]")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^Trial: 1[3-5] days left$"));
        await Expect(plan).ToContainTextAsync("Growth until");
        await Expect(plan.Locator("[data-headroom]")).ToHaveTextAsync($"0 of {Jobs(ShippedPlans.Growth.RequiredLimit)}");
        await Expect(plan.GetByRole(AriaRole.List, new() { Name = "What your plan includes" }).GetByRole(AriaRole.Listitem)).Not.ToHaveCountAsync(0);

        // The plans as cards, each with its price and limit, the trial's plan marked.
        var plans = Page.GetByRole(AriaRole.Region, new() { Name = "Plans", Exact = true });
        await Expect(plans.Locator("[data-plan-card=starter]")).ToContainTextAsync($"${ShippedPlans.Starter.MonthlyPrice:0.00} a month");
        await Expect(plans.Locator("[data-plan-card=starter]")).ToContainTextAsync($"Up to {Jobs(ShippedPlans.Starter.RequiredLimit)}");
        await Expect(plans.Locator("[data-plan-card=growth]")).ToContainTextAsync($"${ShippedPlans.Growth.MonthlyPrice:0.00} a month");
        await Expect(plans.Locator("[data-plan-card=growth]")).ToContainTextAsync($"Up to {Jobs(ShippedPlans.Growth.RequiredLimit)}");
        await Expect(plans.Locator("[data-plan-card=growth]")).ToContainTextAsync("Your trial");
        await Expect(plans.Locator("[data-plan-card=unlimited]")).ToContainTextAsync($"${ShippedPlans.Unlimited.MonthlyPrice:0.00} a month");
        await Expect(plans.Locator("[data-plan-card=unlimited]")).ToContainTextAsync("Unlimited open jobs");
        await Expect(plans.GetByRole(AriaRole.Button, new() { NameRegex = new("^Choose") })).ToHaveCountAsync(3);
        // From about a 1440px window Billing is an overview (design system section 7): the plans are in the side column, right of
        // the plan card, one card above the other.
        var planBox = await BoxAsync(plan);
        (await BoxAsync(plans)).X.Should().BeGreaterThan(planBox.X + planBox.Width, "the plans are in the side column at 1440");
        (await CardTopsAsync(plans)).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems("the cards stack in the side column at 1440");
        (await NoSidewaysScrollAsync()).Should().BeTrue();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-trial-1440");

        // Below that the page is one column, and there the three cards sit side by side under the plan card. The pointer rests
        // where the test last clicked; once the layout moves, a panel title under it would open its tooltip, so it waits in the
        // window's corner.
        await ParkPointerAsync();
        await Page.SetViewportSizeAsync(1200, 1000);
        await Expect(plans.Locator("[data-plan-card=unlimited]")).ToBeVisibleAsync();
        (await BoxAsync(plans)).X.Should().BeApproximately((await BoxAsync(plan)).X, 1, "one column at 1200");
        (await CardTopsAsync(plans)).Distinct().Should().HaveCount(1, "three cards sit side by side in the one column at 1200");

        // On a phone the cards stack, one above the other, and the page never scrolls sideways. Measured once the phone's shell
        // is in, the sidebar a sheet behind its button: until the resize reaches it, the full sidebar still takes 248px.
        await Page.SetViewportSizeAsync(390, 844);
        await PhoneShellAsync();
        await Expect(plans.Locator("[data-plan-card=unlimited]")).ToBeVisibleAsync();
        var phoneTops = await plans.Locator("[data-plan-card]").EvaluateAllAsync<double[]>("cards => cards.map(card => Math.round(card.getBoundingClientRect().top))");
        phoneTops.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems("the cards stack at 390");
        (await NoSidewaysScrollAsync()).Should().BeTrue("nothing reaches past the window: {0}", await PastTheWindowAsync());
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-trial-390");
        await Page.SetViewportSizeAsync(1440, 1000);

        // Choose a plan brings the plans into view for the choice.
        await plan.GetByRole(AriaRole.Button, new() { Name = "Choose a plan" }).ClickAsync();
        await Expect(plans).ToBeFocusedAsync();

        // Choosing a plan leaves for the sandbox's checkout, which activates it and sends the browser back here.
        await plans.GetByRole(AriaRole.Button, new() { Name = "Choose Starter" }).ClickAsync();
        await Expect(band).ToContainTextAsync("Active");
        await Expect(plan.Locator("[data-plan-headline]")).ToHaveTextAsync("Starter");
        await Expect(plan.Locator("[data-plan-price]")).ToContainTextAsync($"${ShippedPlans.Starter.MonthlyPrice:0.00} a month");
        await Expect(plan).ToContainTextAsync("Renews");
        await Expect(plan.Locator("[data-headroom]")).ToHaveTextAsync($"0 of {Jobs(ShippedPlans.Starter.RequiredLimit)}");
        await Expect(plan.GetByRole(AriaRole.Button, new() { Name = "Change plan" })).ToBeVisibleAsync();
        await Expect(plans.Locator("[data-plan-card=starter]")).ToContainTextAsync("Your plan");
        await Expect(plans.GetByRole(AriaRole.Button, new() { NameRegex = new("^Change to") })).ToHaveCountAsync(2);
        // The longest label, Change to Unlimited, stays on one line inside its card (F5): in the side column at 2560 and 1440, and
        // at a laptop width where the page is one column and the three cards side by side are at their narrowest.
        foreach (var width in new[] { 2560, 1440, 1200 })
        {
            await Page.SetViewportSizeAsync(width, 1000);
            var overflowing = await plans.Locator("[data-plan-card] button").EvaluateAllAsync<int>(
                "buttons => buttons.filter(button => button.scrollWidth > button.clientWidth || button.getBoundingClientRect().right > button.closest('[data-plan-card]').getBoundingClientRect().right).length");
            overflowing.Should().Be(0, $"every plan button fits its card at {width}");
        }

        await Page.SetViewportSizeAsync(1440, 1000);

        // This month, added up: the plan's line and the total, then the next invoice. No AI credits bought, so the plan is the whole of it.
        var month = Page.GetByRole(AriaRole.Region, new() { Name = "This month so far", Exact = true });
        var price = $"${ShippedPlans.Starter.MonthlyPrice:0.00}";
        await Expect(month).ToContainTextAsync(new System.Text.RegularExpressions.Regex($@"Starter\s*{System.Text.RegularExpressions.Regex.Escape(price)}"));
        await Expect(month.Locator("[data-total]")).ToHaveTextAsync(price);
        await Expect(month.Locator("[data-next-invoice]")).ToContainTextAsync($"Next invoice about {price} on");

        // The card and the sandbox's sample invoices, newest first, each with its PDF.
        var payment = Page.GetByRole(AriaRole.Region, new() { Name = "Payment method", Exact = true });
        await Expect(payment).ToContainTextAsync("Visa ending in 4242");
        await Expect(payment.GetByRole(AriaRole.Button, new() { Name = "Change card" })).ToBeVisibleAsync();
        await Expect(payment.GetByRole(AriaRole.Button, new() { Name = "Cancel plan" })).ToBeVisibleAsync();
        var invoices = Page.GetByRole(AriaRole.Region, new() { Name = "Invoices", Exact = true });
        await Expect(invoices.GetByRole(AriaRole.Row)).ToHaveCountAsync(4);
        await Expect(invoices.GetByRole(AriaRole.Row).Nth(1)).ToContainTextAsync(new System.Text.RegularExpressions.Regex($@"{System.Text.RegularExpressions.Regex.Escape(price)}\s*Paid"));
        await Expect(invoices.GetByRole(AriaRole.Link, new() { NameRegex = new("^PDF") })).ToHaveCountAsync(3);
        await Expect(plans.GetByRole(AriaRole.Button, new() { NameRegex = new("^Choose") })).ToHaveCountAsync(0);
        // Nothing is left to say once a plan is paid for.
        await Expect(trialEnds).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-active-1440");

        // The overview's columns (design system section 7, S10): the plan, this month and the invoices in the main column, the
        // card and the plans in the side column, each stacking on its own; on a phone one column, main then side.
        await ParkPointerAsync();
        foreach (var width in new[] { 1440, 2560 })
        {
            await Page.SetViewportSizeAsync(width, 1000);
            var planCardBox = await BoxAsync(plan);
            var monthBox = await BoxAsync(month);
            // The panel, named Invoices like the table inside it.
            var invoicesBox = await BoxAsync(invoices.First);
            monthBox.X.Should().BeApproximately(planCardBox.X, 1, "This month so far is in the main column at {0}", width);
            invoicesBox.X.Should().BeApproximately(planCardBox.X, 1, "the invoices are in the main column at {0}", width);
            invoicesBox.Y.Should().BeGreaterThan(monthBox.Y, "the invoices follow this month at {0}", width);
            var paymentBox = await BoxAsync(payment);
            paymentBox.X.Should().BeGreaterThan(planCardBox.X + planCardBox.Width, "the payment method is in the side column at {0}", width);
            paymentBox.Y.Should().BeApproximately(planCardBox.Y, 1, "the two columns start at the same top at {0}", width);
            (await BoxAsync(plans)).X.Should().BeApproximately(paymentBox.X, 1, "the plans are in the side column at {0}", width);
            (await NoSidewaysScrollAsync()).Should().BeTrue();
        }

        await SaveScreenshotAsync(Page, "dashboard-settings-billing-active-2560");
        await Page.SetViewportSizeAsync(390, 844);
        await PhoneShellAsync();
        (await BoxAsync(payment)).Y.Should().BeGreaterThan((await BoxAsync(invoices.First)).Y, "one column on a phone, the side column after the main one");
        (await NoSidewaysScrollAsync()).Should().BeTrue();
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-active-390");
        await Page.SetViewportSizeAsync(1440, 1000);

        // Changing the card opens the payment page, which in the sandbox changes nothing and comes back still subscribed.
        await payment.GetByRole(AriaRole.Button, new() { Name = "Change card" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/billing"));
        await Expect(band).ToContainTextAsync("Active");

        // Billing is one click from the Owner's own menu too.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true }).GetByRole(AriaRole.Link, new() { Name = "General" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("Owner$") }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Billing" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/billing"));
    }

    // ADR-0032: AI use on ScreenHire's key is prepaid credits. Buying goes to the sandbox's Checkout, and the credits arrive only when the
    // worker grants the confirmed payment, which reaches the open page live. A saved card then lets auto-reload be turned on.
    [Fact]
    public async Task An_owner_buys_AI_credits_sees_the_balance_and_the_purchase_and_turns_auto_reload_on()
    {
        var owner = ApiSession.NewEmail("e2e-billing-credits");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await Page.GotoAsync(DashboardUrl("/settings/billing#ai-credits"));

        // A new organization starts with its starting credit, and no card saved yet: auto-reload says why it cannot be turned on.
        var credits = Page.GetByRole(AriaRole.Region, new() { Name = "AI credits", Exact = true });
        var balance = credits.Locator("[data-credit-balance]");
        await Expect(balance).ToHaveTextAsync("$5.00");
        var autoReload = Page.GetByRole(AriaRole.Region, new() { Name = "Auto-reload", Exact = true });
        var reloadSwitch = autoReload.GetByRole(AriaRole.Switch, new() { Name = "Auto-reload" });
        await Expect(reloadSwitch).ToBeDisabledAsync();
        await Expect(autoReload).ToContainTextAsync("Buy credits once to save a card.");

        // Buying $25 goes to the sandbox's Checkout and back. The page says the credits come when the payment is confirmed, then they do.
        await credits.GetByRole(AriaRole.Button, new() { Name = "$25.00 of AI credits" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/settings/billing"));
        await Expect(balance).ToHaveTextAsync("$30.00", new() { Timeout = 30_000 });

        var purchases = Page.GetByRole(AriaRole.Region, new() { Name = "Purchases", Exact = true });
        await Expect(purchases.GetByRole(AriaRole.Row)).ToHaveCountAsync(3, new() { Timeout = 30_000 });
        var table = purchases.GetByRole(AriaRole.Table);
        await Expect(table).ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"Bought\s*\$25\.00\s*Paid"));
        await Expect(table).ToContainTextAsync("Starting credit");

        // The purchase saved the sandbox's card, so auto-reload can be turned on and saved.
        await Expect(reloadSwitch).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await reloadSwitch.ClickAsync();
        await autoReload.GetByLabel("When the balance falls below").FillAsync("5.00");
        await autoReload.GetByLabel("Add", new() { Exact = true }).FillAsync("25.00");
        await autoReload.GetByLabel("Most in a month").FillAsync("100.00");
        await autoReload.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(autoReload.GetByRole(AriaRole.Status)).ToHaveTextAsync("Saved. Auto-reload is on.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-credits-1440");

        // It stays on.
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Auto-reload", Exact = true }).GetByRole(AriaRole.Switch, new() { Name = "Auto-reload" }))
            .ToHaveAttributeAsync("aria-checked", "true");
    }

    [Fact]
    public async Task A_publish_past_the_plans_limit_shows_the_plan_message_and_a_link_to_Billing()
    {
        var owner = ApiSession.NewEmail("e2e-billing-limit");
        var limit = ShippedPlans.Starter.RequiredLimit;
        Guid first;
        Guid second;
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);

            // Fill the Starter plan's open jobs: subscribe the way the dashboard does, by starting a checkout and following its link.
            using var checkout = await session.PostAsync("/v1/organization/billing/checkout", new { plan = "starter" }, Ct);
            checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync(Ct));
            var link = (await checkout.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("url").GetString()!;
            using var browser = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = session.BaseAddress };
            using var activated = await browser.GetAsync(new Uri(session.BaseAddress, link), Ct);
            activated.StatusCode.Should().Be(HttpStatusCode.Redirect);

            first = Guid.Empty;
            for (var i = 1; i <= limit; i++)
            {
                var job = await CreateJobAsync(session, $"Platform Engineer {i}");
                using var published = await session.PostAsync($"/v1/jobs/{job}/publish", new { }, Ct);
                published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Ct));
                first = first == Guid.Empty ? job : first;
            }

            second = await CreateJobAsync(session, "Design Engineer");
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, owner);
        await Page.GotoAsync(DashboardUrl($"/jobs/{second}"));
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Publish to") }).ClickAsync();

        var notice = Page.Locator("[data-slot=notice]");
        await Expect(notice).ToContainTextAsync($"Your plan allows {Jobs(limit)} and {limit} {(limit == 1 ? "is" : "are")} open.");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-job-plan-limit-1440");

        // The first job is untouched, and the second is still a draft.
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            (await StatusAsync(session, first)).Should().Be("Open");
            (await StatusAsync(session, second)).Should().Be("Draft");
        }

        await notice.GetByRole(AriaRole.Link, new() { Name = "Upgrade plan" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/billing"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Billing");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Plan", Exact = true })).ToContainTextAsync($"{limit} of {limit}");
    }

    [Fact]
    public async Task A_recruiter_has_no_Billing_entry_and_is_told_it_is_for_owners()
    {
        var owner = ApiSession.NewEmail("e2e-billing-owner");
        var recruiter = ApiSession.NewEmail("e2e-billing-recruiter");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            using var invited = await session.PostAsync("/v1/organization/invitations", new { email = recruiter, role = "Recruiter" }, Ct);
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
        }

        var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(recruiter, Ct);
        using (var session = new ApiSession(Fixture))
        {
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token, name = ApiSession.NewName("Jordan") }, Ct);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, recruiter);
        var main = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveCountAsync(0);
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Billing" })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("Recruiter$") }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menu)).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Billing" })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");

        await GoToAsync("dashboard", "/settings/billing");
        await Expect(Page.GetByText("Not in your role")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Only an owner of your organization can change billing.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Change card" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Choose") })).ToHaveCountAsync(0);
    }

    // Measured against the window's width, which includes the stable scrollbar gutter (S8): a page that fits is never wider than it.
    private async Task<bool> NoSidewaysScrollAsync() =>
        await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth");

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
    {
        await Expect(locator).ToBeVisibleAsync();
        return (await locator.BoundingBoxAsync())!;
    }

    // What reaches past the window's right edge and is not cut by a scrolling ancestor, so a sideways scroll names its cause.
    private Task<string> PastTheWindowAsync() => Page.EvaluateAsync<string>(
        """
        () => {
          const width = document.documentElement.clientWidth;
          const clipped = el => { for (let a = el.parentElement; a; a = a.parentElement) if (getComputedStyle(a).overflowX !== 'visible') return true; return false; };
          return JSON.stringify([width, document.documentElement.scrollWidth, ...[...document.querySelectorAll('body *')]
            .filter(el => { const r = el.getBoundingClientRect(); return r.right > width + 0.5 && r.width > 0 && !clipped(el); })
            .slice(0, 10).map(el => [el.tagName, String(el.className?.baseVal ?? el.className).slice(0, 100), Math.round(el.getBoundingClientRect().right)])]);
        }
        """);

    private Task ParkPointerAsync() => Page.Mouse.MoveAsync(0, 0);

    private Task PhoneShellAsync() =>
        Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" })).ToHaveCountAsync(0);

    private static Task<double[]> CardTopsAsync(ILocator plans) =>
        plans.Locator("[data-plan-card]").EvaluateAllAsync<double[]>("cards => cards.map(card => Math.round(card.getBoundingClientRect().top))");

    private static string Jobs(int count) => count == 1 ? "1 open job" : $"{count} open jobs";

    private static async Task<Guid> CreateJobAsync(ApiSession session, string title)
    {
        using var response = await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description = Description,
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = UkAndIreland,
                salary = new { min = 60_000, max = 75_000, currency = "GBP", period = "Year" },
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<string?> StatusAsync(ApiSession session, Guid jobId)
    {
        using var response = await session.Client.GetAsync($"/v1/jobs/{jobId}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString();
    }

    private async Task SignInAsync(IPage page, string email)
    {
        await page.GotoAsync(DashboardUrl("/sign-in"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await page.GetByLabel("Any email address").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
