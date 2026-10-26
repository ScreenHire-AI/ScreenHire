using System.Net;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The Templates page in a real browser (design system section 21): one tab per kind of template, email templates moved
/// there from Settings with the old address redirecting, Reset to original on a built-in email the team edited, a
/// scorecard edited in the form builder and seen in Preview and History, and archiving and restoring a template. Each
/// test signs in as a new person with a new organization.
/// </summary>
public sealed class TemplatesPageTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string NewLabel = "Meets the job's requirements";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILocator HeaderBand => Page.Locator("[data-slot=header-band]");

    [Fact]
    public async Task Email_templates_are_a_tab_of_the_Templates_page_and_the_old_settings_address_leads_there()
    {
        var email = await OnboardAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        // Settings has no Email templates any more.
        await Page.GotoAsync(DashboardUrl("/settings/organization"));
        var menu = Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings" });
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Webhooks" })).ToBeVisibleAsync();
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Email templates" })).ToHaveCountAsync(0);

        // The old address lands on the Emails tab, and so does an old template's.
        await Page.GotoAsync(DashboardUrl("/settings/email-templates"));
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/templates?tab=emails"));
        await Expect(Page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex("^Emails") })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Region("Email templates").GetByRole(AriaRole.Link, new() { Name = "Application received" })).ToBeVisibleAsync();
        await Page.GotoAsync(DashboardUrl("/settings/email-templates/stage-change"));
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/templates/emails/stage-change"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Moved to the next step");

        // Each tab says what its templates are for and passes axe, on a laptop and on a phone.
        foreach (var (width, height) in new[] { (1440, 1000), (360, 800) })
        {
            await Page.SetViewportSizeAsync(width, height);
            foreach (var (tab, sentence) in new[]
            {
                ("hiring", "Where a job starts"),
                ("scorecards", "What reviewers fill in at a stage"),
                ("emails", "What candidates receive"),
            })
            {
                await Page.GotoAsync(DashboardUrl(tab == "hiring" ? "/templates" : $"/templates?tab={tab}"));
                await Expect(Page.GetByText(sentence).First).ToBeAttachedAsync();
                await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "Show" }).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^In use") }))
                    .ToContainTextAsync(new Regex(@"\d"));
                (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue($"{tab} at {width} scrolls sideways");
                await ExpectNoAccessibilityViolationsAsync();
                await SaveScreenshotAsync(Page, $"dashboard-templates-{tab}-{width}");
            }
        }
    }

    [Fact]
    public async Task An_owner_resets_a_built_in_email_the_team_edited_after_a_confirm_and_History_records_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var email = await OnboardAsync(async session =>
        {
            using var edited = await session.PutAsync(
                "/v1/email-templates/stage-change", new { subject = $"Next steps {marker} for {{{{ jobTitle }}}}", body = "Hi {{ firstName }}" }, Ct);
            edited.StatusCode.Should().Be(HttpStatusCode.OK);
        });
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        await Page.GotoAsync(DashboardUrl("/templates?tab=emails"));
        var row = Region("Email templates").GetByRole(AriaRole.Row).Filter(new() { HasText = "Moved to the next step" });
        await Expect(row).ToContainTextAsync("by you");
        await row.GetByRole(AriaRole.Button, new() { Name = "Actions for Moved to the next step" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Reset to original" }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Reset Moved to the next step to the original?" });
        await ExpectNoAccessibilityViolationsAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Reset to original" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "back to ScreenHire's wording" })).ToBeVisibleAsync();

        // Reset leaves the menu, the wording is ScreenHire's again, and History says so.
        await row.GetByRole(AriaRole.Button, new() { Name = "Actions for Moved to the next step" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Duplicate" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Reset to original" })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");
        await row.GetByRole(AriaRole.Link, new() { Name = "Moved to the next step" }).ClickAsync();
        await Expect(Page.GetByLabel("Subject")).Not.ToHaveValueAsync(new Regex(marker));
        await HeaderBand.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        var history = Page.GetByRole(AriaRole.Dialog, new() { Name = "History" });
        await Expect(history.GetByRole(AriaRole.Listitem).First).ToContainTextAsync("You restored the original wording");
    }

    [Fact]
    public async Task An_owner_edits_a_scorecard_sees_the_edit_in_Preview_and_finds_it_in_History_then_archives_and_restores_it()
    {
        var email = await OnboardAsync();
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(email);

        // A built-in reads only and says why; Duplicate makes the team's own copy and opens it.
        await Page.GotoAsync(DashboardUrl("/templates?tab=scorecards"));
        var list = Region("Scorecards");
        await list.GetByRole(AriaRole.Link, new() { Name = "Screening", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Copy this ScreenHire scorecard to change it.", new() { Exact = false })).ToBeVisibleAsync();
        await Page.GoBackAsync();
        await list.GetByRole(AriaRole.Button, new() { Name = "Actions for Screening" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Duplicate" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Screening (copy)");

        // Edit a question in the form builder and save.
        var questions = Region("Questions");
        await questions.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Edit question 1: ") }).ClickAsync();
        await questions.GetByRole(AriaRole.Textbox, new() { Name = "Question 1", Exact = true }).FillAsync(NewLabel);
        await ExpectNoAccessibilityViolationsAsync();
        await HeaderBand.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(HeaderBand.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved." })).ToBeVisibleAsync();

        // Preview shows the scorecard as a reviewer fills it in, in place of the builder (A8).
        await Page.GetByRole(AriaRole.Group, new() { Name = "View" }).GetByRole(AriaRole.Button, new() { Name = "Preview" }).ClickAsync();
        await Expect(questions).ToHaveCountAsync(0);
        var preview = Region("Scorecard for Jordan Lee");
        await Expect(preview).ToContainTextAsync(NewLabel);
        await Expect(preview).ToContainTextAsync("Overall recommendation");
        await ExpectNoAccessibilityViolationsAsync();
        await SaveScreenshotAsync(Page, "dashboard-scorecard-template-preview");

        // History lists the edit.
        await HeaderBand.GetByRole(AriaRole.Button, new() { Name = "History" }).ClickAsync();
        var history = Page.GetByRole(AriaRole.Dialog, new() { Name = "History" });
        await Expect(history.GetByRole(AriaRole.Listitem).First).ToContainTextAsync("You changed the questions");
        await Page.Keyboard.PressAsync("Escape");

        // Archive takes it out of All and puts it under Archived, until Restore.
        await Page.GotoAsync(DashboardUrl("/templates?tab=scorecards"));
        await list.GetByRole(AriaRole.Button, new() { Name = "Actions for Screening (copy)" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Archive" }).ClickAsync();
        await Expect(list.GetByRole(AriaRole.Link, new() { Name = "Screening (copy)" })).ToHaveCountAsync(0);
        var filters = Page.GetByRole(AriaRole.Group, new() { Name = "Show" });
        await filters.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Archived") }).ClickAsync();
        await list.GetByRole(AriaRole.Button, new() { Name = "Actions for Screening (copy)" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Restore" }).ClickAsync();
        await filters.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^All") }).ClickAsync();
        await Expect(list.GetByRole(AriaRole.Link, new() { Name = "Screening (copy)" })).ToBeVisibleAsync();
    }

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    // A new person and organization, with anything `arrange` adds through the API as that person.
    private async Task<string> OnboardAsync(Func<ApiSession, Task>? arrange = null)
    {
        var email = ApiSession.NewEmail("e2e-templates");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        using (var onboarded = await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Fernside Bakery", slug = $"test-{Guid.NewGuid():N}" }, Ct))
        {
            onboarded.StatusCode.Should().Be(HttpStatusCode.OK, await onboarded.Content.ReadAsStringAsync(Ct));
        }

        if (arrange is not null)
        {
            await arrange(session);
        }

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
