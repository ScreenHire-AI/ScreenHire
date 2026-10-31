using System.Text.RegularExpressions;
using ScreenHire.Domain.Applications;

namespace ScreenHire.E2E;

/// <summary>
/// Reports (docs/architecture/reports.md): a hiring team opens Reports from the sidebar, reads its funnel, time to hire
/// and sources, switches a chart to its table, widens the dates and narrows to one job. Signs in as a new person with a
/// new organization whose history is written already dated.
/// </summary>
public sealed class ReportsTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_funnel_time_to_hire_and_sources_follow_the_dates_and_jobs_picked()
    {
        var email = await ArrangeAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Reports" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Reports");

        // The last 30 days: three applications arrived, one is hired; two hires were made, 10 and 40 days after applying.
        var funnel = Region("Hiring funnel");
        await Expect(funnel.GetByRole(AriaRole.Img, new() { Name = "Applied: 3 applications, 67% moved on" })).ToBeVisibleAsync();
        await Expect(funnel.GetByRole(AriaRole.Img, new() { Name = "Hired: 1 application" })).ToBeVisibleAsync();
        await Expect(Region("Time to hire")).ToContainTextAsync("Median across 2 hires");
        await Expect(Region("Time to hire").GetByRole(AriaRole.Img, new() { Name = "Platform Engineer: 25 days, 2 hires" })).ToBeVisibleAsync();
        await Expect(Region("Sources").GetByRole(AriaRole.Img, new() { Name = "Careers page: 2 applications, 1 hire" })).ToBeVisibleAsync();
        await Expect(Region("Sources").GetByRole(AriaRole.Img, new() { Name = "Added by a recruiter: 1 application, 0 hires" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-reports-1440");

        // Every chart has its table.
        await funnel.GetByRole(AriaRole.Button, new() { Name = "Table" }).ClickAsync();
        await Expect(funnel.GetByRole(AriaRole.Table).GetByRole(AriaRole.Row)).ToHaveCountAsync(6);
        await Expect(funnel.GetByRole(AriaRole.Table)).ToContainTextAsync("Moved on");
        await funnel.GetByRole(AriaRole.Button, new() { Name = "Chart" }).ClickAsync();

        // The last 90 days takes in the application from 45 days ago, and the address keeps the choice.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Dates: Last 30 days" }).ClickAsync();
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Last 90 days" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("range=90"));
        await Expect(funnel.GetByRole(AriaRole.Img, new() { Name = "Applied: 4 applications, 75% moved on" })).ToBeVisibleAsync();

        // One job: Data Engineer's single application, in Applied.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Job: All" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Data Engineer" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(funnel.GetByRole(AriaRole.Img, new() { Name = "Applied: 1 application, 0% moved on" })).ToBeVisibleAsync();
        await Expect(Region("Time to hire")).ToContainTextAsync("No hires in these dates");

        // A phone: one column, nothing scrolls sideways.
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(funnel).ToBeVisibleAsync();
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            .Should().BeTrue("the page at {0} scrolls sideways", Page.Url);
        await SaveScreenshotAsync(Page, "dashboard-reports-390");
    }

    // A new person and organization with two jobs and their dated history: Platform Engineer, where two people were hired
    // and one rejected, and Data Engineer, with one application in Applied.
    private async Task<string> ArrangeAsync()
    {
        var email = ApiSession.NewEmail("e2e-reports");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        var organizationId = await session.OnboardAsync(Ct);
        var platform = TestData.NewJob(organizationId, $"platform-{Guid.NewGuid():N}", "Platform Engineer");
        var data = TestData.NewJob(organizationId, $"data-{Guid.NewGuid():N}", "Data Engineer");
        await Fixture.AddJobsAsync(organizationId, [platform, data], Ct);

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset Ago(int days) => now.AddDays(-days);
        await Fixture.AddApplicationHistoryAsync(
            organizationId, platform.Id, Ago(20), ApplicationSource.CareersSite, [("screening", Ago(18)), ("interview", Ago(15)), ("offer", Ago(12)), ("hired", Ago(10))], Ct);
        await Fixture.AddApplicationHistoryAsync(
            organizationId, platform.Id, Ago(25), ApplicationSource.Recruiter, [("screening", Ago(24)), ("interview", Ago(20)), ("rejected", Ago(19))], Ct);
        await Fixture.AddApplicationHistoryAsync(
            organizationId, platform.Id, Ago(45), ApplicationSource.CareersSite, [("interview", Ago(40)), ("hired", Ago(5))], Ct);
        await Fixture.AddApplicationHistoryAsync(organizationId, data.Id, Ago(8), ApplicationSource.CareersSite, cancellationToken: Ct);
        return email;
    }

    private async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(new Uri(Fixture.GetEndpoint("dashboard"), "/sign-in").ToString());
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });
}
