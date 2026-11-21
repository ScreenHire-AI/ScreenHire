using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Sharing a view is an offer (docs/architecture/saved-views.md, design system sections 8 and 21), in one new organization
/// with an Owner and a Recruiter. The Owner shares a saved view: it is not in the Recruiter's sidebar, but Browse team views
/// lists it with a New mark, who shared it, its filters and how many candidates it shows. The Recruiter previews it (the
/// page opens, the sidebar still has nothing), adds it (the sidebar lists it, and the New mark is gone the next time they
/// open the sheet), and removes it from the sidebar's menu (it leaves the sidebar and stays on offer).
/// </summary>
public sealed class ViewSharingTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string ViewName = "Team referrals";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shared_view_waits_in_Browse_team_views_until_a_teammate_previews_adds_and_removes_it()
    {
        using var ownerSession = new ApiSession(Fixture);
        await ownerSession.SignInWithDevSsoAsync(ApiSession.NewEmail("owner"), Ct);
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { name = "Casey Morgan", organizationName = "Harbour Lane Studio", slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        var recruiterEmail = ApiSession.NewEmail("recruiter");
        using var recruiterSession = await InviteAsync(ownerSession, recruiterEmail, "Recruiter");

        // The Owner saves a view and shares it with the team.
        var view = await ReadAsync(await ownerSession.PostAsync(
            "/v1/views",
            new
            {
                name = ViewName,
                scope = "Candidates",
                jobId = (Guid?)null,
                shared = true,
                filters = new
                {
                    baseView = "needs-review",
                    jobIds = Array.Empty<Guid>(),
                    stages = Array.Empty<string>(),
                    authenticity = (string?)null,
                    aiAssistance = (string?)null,
                    tag = "referral",
                    sortColumn = (string?)null,
                    sortDescending = false,
                },
            },
            Ct), HttpStatusCode.Created);
        var viewId = view.GetProperty("id").GetGuid();

        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, recruiterEmail);
        var sidebar = Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });

        // Sharing offered it and added it to nobody's sidebar.
        await Expect(sidebar.GetByRole(AriaRole.Button, new() { Name = "Browse team views" })).ToBeVisibleAsync();
        await Expect(sidebar.GetByRole(AriaRole.Link, new() { Name = ViewName })).ToHaveCountAsync(0);

        // Browse team views lists it: who shared it, its filters in one line, how many candidates it shows, and New.
        await sidebar.GetByRole(AriaRole.Button, new() { Name = "Browse team views" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Team views" });
        var row = sheet.GetByRole(AriaRole.Listitem).Filter(new() { HasText = ViewName });
        await Expect(row).ToContainTextAsync("Shared by");
        await Expect(row).ToContainTextAsync("To review, tagged referral");
        await Expect(row).ToContainTextAsync("Shows 0 candidates now", new() { Timeout = 30_000 });
        await Expect(row.GetByText("New", new() { Exact = true })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Preview opens the view's page and adds nothing.
        await row.GetByRole(AriaRole.Link, new() { Name = $"Preview {ViewName}" }).ClickAsync();
        await Page.WaitForURLAsync(new Regex($"/candidates\\?view={viewId}$"));
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(sidebar.GetByRole(AriaRole.Link, new() { Name = ViewName })).ToHaveCountAsync(0);

        // Add puts it in the sidebar, and the New mark is gone on the next visit.
        await sidebar.GetByRole(AriaRole.Button, new() { Name = "Browse team views" }).ClickAsync();
        await Expect(row.GetByText("New", new() { Exact = true })).ToHaveCountAsync(0);
        await row.GetByRole(AriaRole.Button, new() { Name = $"Add {ViewName} to your sidebar" }).ClickAsync();
        await Expect(row).ToContainTextAsync("In your sidebar");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(sheet).ToBeHiddenAsync();
        var added = sidebar.GetByRole(AriaRole.Link, new() { NameRegex = new Regex($"^{ViewName}") });
        await Expect(added).ToBeVisibleAsync();

        // Remove from sidebar leaves it shared, so the sheet still offers it.
        await sidebar.GetByRole(AriaRole.Button, new() { Name = $"Actions for {ViewName}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove from sidebar" }).ClickAsync();
        await Expect(added).ToHaveCountAsync(0);
        await sidebar.GetByRole(AriaRole.Button, new() { Name = "Browse team views" }).ClickAsync();
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Add {ViewName} to your sidebar" })).ToBeVisibleAsync();
    }

    private async Task<ApiSession> InviteAsync(ApiSession owner, string email, string role)
    {
        using (var invited = await owner.PostAsync("/v1/organization/invitations", new { email, role }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
        }

        var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct);
        var session = new ApiSession(Fixture);
        try
        {
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token, name = "Jordan Lee" }, Ct);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    // Dev SSO finds the account by its address, so the invited person signs in to the organization they joined.
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
