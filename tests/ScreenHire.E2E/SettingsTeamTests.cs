using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The people in Settings, in a real browser: the sidebar's one Settings item opens the Settings menu; an owner invites
/// a colleague, starting from Viewer, who accepts through the Mailpit email, then changes their role and removes them;
/// Compare roles opens the grid of what each role can do; an owner makes an Interviewer role and gives it to a member,
/// who can then add a note but not move a candidate; and a Recruiter is told their role cannot change settings. Each
/// test but the last signs in as a new owner of a new organization.
/// </summary>
public sealed class SettingsTeamTests(E2EFixture fixture) : SettingsArea(fixture)
{
    // The three sidebar items Settings used to be; one Settings item replaced them.
    private static readonly string[] RetiredAreas = ["Organization", "Hiring setup", "Integrations"];

    [Fact]
    public async Task An_owner_invites_a_colleague_who_accepts_from_the_email_then_changes_their_role_and_removes_them()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);

        // Settings is one sidebar item that opens the first settings page, General for an Owner, with every page in its menu.
        var main = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveCountAsync(1);
        foreach (var retired in RetiredAreas)
        {
            await Expect(main.GetByRole(AriaRole.Link, new() { Name = retired, Exact = true })).ToHaveCountAsync(0);
        }

        await Expect(main.GetByRole(AriaRole.Link, new() { Name = "Team" })).ToHaveCountAsync(0);
        await main.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/organization"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("General");
        var menu = Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings", Exact = true });
        await menu.GetByRole(AriaRole.List, new() { Name = "Organization", Exact = true }).GetByRole(AriaRole.Link, new() { Name = "Team" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/team"));
        await Expect(menu.GetByRole(AriaRole.Link, new() { Name = "Team" })).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Team");
        var members = Page.GetByRole(AriaRole.Region, new() { Name = "Members" });
        await Expect(members.GetByRole(AriaRole.Row).Filter(new() { HasText = owner })).ToContainTextAsync("Owner");
        await ExpectNoAccessibilityViolationsAsync();

        var colleague = ApiSession.NewEmail("colleague");
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "Invite" }).ClickAsync();
        var invite = Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite a colleague" });
        // The sheet starts at Viewer, the least access, and says what each role is for.
        await Expect(invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Viewer") })).ToBeCheckedAsync();
        await Expect(invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Recruiter") })).ToHaveAccessibleNameAsync(new Regex("Works the jobs"));
        await invite.GetByLabel("Email", new() { Exact = true }).FillAsync(colleague);
        await invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Recruiter") }).CheckAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await invite.GetByRole(AriaRole.Button, new() { Name = "Send invitation" }).ClickAsync();
        await Expect(invite).ToBeHiddenAsync();
        var invitations = Page.GetByRole(AriaRole.Region, new() { Name = "Invitations" });
        await Expect(invitations.GetByRole(AriaRole.Row).Filter(new() { HasText = colleague })).ToContainTextAsync("Recruiter");

        // The colleague opens the emailed link in their own browser.
        var link = await Fixture.Mailpit.WaitForInvitationLinkAsync(colleague, Ct);
        await using (var theirs = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" }))
        {
            var page = await theirs.NewPageAsync();
            await page.GotoAsync(link.ToString());
            await page.GetByRole(AriaRole.Button, new() { Name = "Accept invitation" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
            // A Recruiter has no Settings in the sidebar, but has Verification (read-only).
            var theirNavigation = page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
            await Expect(theirNavigation.GetByRole(AriaRole.Link, new() { Name = "Jobs" })).ToBeVisibleAsync();
            await Expect(theirNavigation.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveCountAsync(0);
            await Expect(theirNavigation.GetByRole(AriaRole.Link, new() { Name = "Verification", Exact = true })).ToBeVisibleAsync();
        }

        await Page.ReloadAsync();
        var row = members.GetByRole(AriaRole.Row).Filter(new() { HasText = colleague });
        await Expect(row).ToContainTextAsync("Recruiter");
        await Expect(invitations).ToContainTextAsync("No open invitations.");

        // Change their role in the access sheet the row's menu opens, with the jobs they see beside it.
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit access" }).ClickAsync();
        var access = Page.GetByRole(AriaRole.Dialog, new() { NameRegex = new("access$") });
        await access.GetByRole(AriaRole.Combobox, new() { Name = "Role" }).SelectOptionAsync(new SelectOptionValue { Label = "Viewer" });
        await access.GetByRole(AriaRole.Button, new() { Name = "Save access" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Viewer");

        // Removing is destructive: it is in the menu and confirmed in a dialog (A2).
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove from team" }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Dialog);
        await Expect(confirm).ToContainTextAsync("Remove");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Remove from team" }).ClickAsync();
        await Expect(row).ToHaveCountAsync(0);
        await Expect(members.GetByRole(AriaRole.Row).Filter(new() { HasText = owner })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Compare_roles_in_the_invite_sheet_opens_what_each_role_can_do()
    {
        var (owner, _) = await OwnerAsync();
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/team");

        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "Invite" }).ClickAsync();
        var invite = Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite a colleague" });
        await Expect(invite.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Viewer") })).ToBeCheckedAsync();
        await invite.GetByRole(AriaRole.Link, new() { Name = "Compare roles" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(DashboardUrl("/settings/roles/compare"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Compare roles");
        var grid = Page.GetByRole(AriaRole.Region, new() { Name = "What each role can do" }).GetByRole(AriaRole.Table);
        // From the API's catalogue: a Recruiter moves candidates, a Viewer does not, and only an Owner manages Owners.
        await Expect(grid.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Rowheader, new() { Name = "Move candidates", Exact = true }) }))
            .ToContainTextAsync(new Regex(@"Yes\s*Yes\s*Yes\s*No"));
        await Expect(grid.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Rowheader, new() { Name = "Manage Owners", Exact = true }) }))
            .ToContainTextAsync(new Regex(@"Yes\s*No\s*No\s*No"));
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task An_owner_makes_an_interviewer_role_and_gives_it_to_a_member()
    {
        // A new organization with one candidate, and a colleague who joins as a Viewer from the emailed invitation.
        var owner = ApiSession.NewEmail("e2e-owner");
        var colleague = ApiSession.NewEmail("interviewer");
        string applicationId;
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(owner, Ct);
            await session.OnboardAsync(Ct);
            using var job = await session.PostAsync(
                "/v1/jobs",
                new { title = "Platform Engineer", description = "<p>Keep the platform running.</p>", employmentType = "FullTime", workplaceType = "Remote" },
                Ct);
            job.StatusCode.Should().Be(HttpStatusCode.Created);
            var jobId = (await job.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();
            using var application = await session.PostAsync(
                $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = "Casey Morgan", email = ApiSession.NewEmail("candidate") } }, Ct);
            application.StatusCode.Should().Be(HttpStatusCode.Created);
            applicationId = (await application.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
            using var invited = await session.PostAsync("/v1/organization/invitations", new { email = colleague, role = "Viewer" }, Ct);
            invited.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await using var theirs = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var theirPage = await theirs.NewPageAsync();
        await theirPage.GotoAsync((await Fixture.Mailpit.WaitForInvitationLinkAsync(colleague, Ct)).ToString());
        await theirPage.GetByRole(AriaRole.Button, new() { Name = "Accept invitation" }).ClickAsync();
        await Expect(theirPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");

        // The owner makes the role on the Roles page: a copy of Viewer, then two more permissions on its own page.
        await SignInAsync(owner);
        await GoToAsync("dashboard", "/settings/roles");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Roles");
        await Page.Locator("[data-slot=header-band]").GetByRole(AriaRole.Button, new() { Name = "New role" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "New role" });
        await Expect(sheet.GetByRole(AriaRole.Radio, new() { NameRegex = new("^Viewer") })).ToBeCheckedAsync();
        await sheet.GetByLabel("Name").FillAsync("Interviewer");
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Create and edit" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/settings/roles/[0-9a-f-]{36}$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Interviewer");
        await Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Add notes and tags") }).CheckAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new("^Fill in scorecards") }).CheckAsync();
        // Managing Owners, giving a key verification access and managing billing: all three are Owner-only (ADR-0021, ADR-0029).
        await Expect(Page.GetByText("Owner only. A custom role cannot have it.")).ToHaveCountAsync(3);
        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Saved.")).ToBeVisibleAsync();

        // Team gives it to the colleague, and shows it by its name alone (W6).
        await GoToAsync("dashboard", "/settings/team");
        var row = Page.GetByRole(AriaRole.Region, new() { Name = "Members" }).GetByRole(AriaRole.Row).Filter(new() { HasText = colleague });
        await Expect(row).ToContainTextAsync("Viewer");
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit access" }).ClickAsync();
        var access = Page.GetByRole(AriaRole.Dialog, new() { NameRegex = new("access$") });
        await access.GetByRole(AriaRole.Combobox, new() { Name = "Role" }).SelectOptionAsync(new SelectOptionValue { Label = "Interviewer" });
        await access.GetByRole(AriaRole.Button, new() { Name = "Save access" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Interviewer");
        await Expect(row).Not.ToContainTextAsync("Custom");

        // The colleague can now add a note to the candidate, on the Activity tab, and is not offered a move.
        await theirPage.GotoAsync(DashboardUrl($"/applications/{applicationId}?tab=activity"));
        await Expect(theirPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Casey Morgan");
        await Expect(theirPage.GetByRole(AriaRole.Region, new() { Name = "Activity" }).GetByLabel("Note for the hiring team")).ToBeVisibleAsync();
        await Expect(theirPage.GetByRole(AriaRole.Button, new() { Name = "Move to stage, Casey Morgan" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_recruiter_sees_no_settings_and_is_told_their_role_cannot_change_them()
    {
        const string recruiter = "jordan@brightwater.example.com"; // the seeded Brightwater Recruiter (DevSso/config.json)
        await SignInAsync(recruiter);

        var navigation = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
        await Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Settings", Exact = true })).ToHaveCountAsync(0);
        await Expect(navigation.GetByRole(AriaRole.Link, new() { Name = "Verification", Exact = true })).ToBeVisibleAsync();

        await GoToAsync("dashboard", "/settings/team");
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Not in your role" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Members" })).ToHaveCountAsync(0);
        await ExpectNoAccessibilityViolationsAsync();
    }
}
