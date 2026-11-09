using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// Settings > Billing in a real browser, against the local sandbox payment provider and the plans the catalogue ships
/// (docs/architecture/billing.md, ADR-0029, read through <see cref="ShippedPlans"/>): Starter, Growth (the trial's plan)
/// and Unlimited. An Owner sees the free trial and the three plans, chooses one through the sandbox's checkout and comes
/// back subscribed; a publish past the plan's limit is refused with a notice that links to Billing; a Recruiter cannot open the
/// page. Each test signs in as a new person with a new organization, which starts on its own 14-day trial.
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

        // Billing is on the Settings home under Organization, for an owner.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Region, new() { Name = "Organization", Exact = true }).GetByRole(AriaRole.Link, new() { Name = "Billing" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/billing"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Billing");

        // A new organization is on the free trial, on the trial plan, with nothing open yet.
        var band = Page.Locator("[data-slot=header-band]");
        await Expect(band).ToContainTextAsync("Free trial");
        await Expect(band.GetByRole(AriaRole.Button, new() { Name = "Manage billing" })).ToHaveCountAsync(0);
        var plan = Page.GetByRole(AriaRole.Region, new() { Name = "Plan", Exact = true });
        await Expect(plan).ToContainTextAsync("Growth");
        await Expect(plan).ToContainTextAsync("Trial ends");
        await Expect(plan).ToContainTextAsync($"0 of {ShippedPlans.Growth.RequiredLimit}");

        var plans = Page.GetByRole(AriaRole.Region, new() { Name = "Plans", Exact = true });
        await Expect(plans).ToContainTextAsync("Starter");
        await Expect(plans).ToContainTextAsync($"${ShippedPlans.Starter.MonthlyPrice:0.##} a month");
        await Expect(plans).ToContainTextAsync($"Up to {Jobs(ShippedPlans.Starter.RequiredLimit)}");
        await Expect(plans).ToContainTextAsync("Growth");
        await Expect(plans).ToContainTextAsync($"${ShippedPlans.Growth.MonthlyPrice:0.##} a month");
        await Expect(plans).ToContainTextAsync($"Up to {Jobs(ShippedPlans.Growth.RequiredLimit)}");
        await Expect(plans).ToContainTextAsync("Unlimited");
        await Expect(plans).ToContainTextAsync($"${ShippedPlans.Unlimited.MonthlyPrice:0.##} a month");
        await Expect(plans).ToContainTextAsync("Unlimited open jobs");
        await Expect(plans.GetByRole(AriaRole.Button, new() { NameRegex = new("^Choose") })).ToHaveCountAsync(3);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-trial-1440");

        // Choosing a plan leaves for the sandbox's checkout, which activates it and sends the browser back here.
        await plans.GetByRole(AriaRole.Button, new() { Name = "Choose Starter" }).ClickAsync();
        await Expect(band).ToContainTextAsync("Active");
        await Expect(band.GetByRole(AriaRole.Button, new() { Name = "Manage billing" })).ToBeVisibleAsync();
        await Expect(plan).ToContainTextAsync("Starter");
        await Expect(plan).ToContainTextAsync("Next payment");
        await Expect(plan).ToContainTextAsync($"0 of {ShippedPlans.Starter.RequiredLimit}");
        await Expect(plans.GetByRole(AriaRole.Button, new() { NameRegex = new("^Choose") })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-settings-billing-active-1440");
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
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
        }

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, recruiter);
        var main = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveCountAsync(0);
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Billing" })).ToHaveCountAsync(0);

        await GoToAsync("dashboard", "/settings/billing");
        await Expect(Page.GetByText("Not in your role")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Only an owner of your organization can change billing.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Manage billing" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Choose") })).ToHaveCountAsync(0);
    }

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
