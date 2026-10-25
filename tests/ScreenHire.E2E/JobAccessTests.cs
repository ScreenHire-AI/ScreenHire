using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Job access through each job's hiring team (ADR-0015), in the browser: an Owner puts a member on Only jobs they're on
/// onto one job through its Hiring team sheet; the member sees that job and nothing of the other, whose address says
/// it is not found; Team says "1 job" and the access sheet lists it; Copy from another job then brings the member onto
/// the second job too.
/// </summary>
public sealed class JobAccessTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private const string Organization = "Fernway Analytics";
    private const string TheirJob = "Platform Engineer";
    private const string OtherJob = "Payroll Specialist";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_on_only_their_jobs_sees_the_jobs_a_hiring_team_gives_them_and_nothing_else()
    {
        using var ownerSession = new ApiSession(Fixture);
        var ownerEmail = ApiSession.NewEmail("owner");
        await ownerSession.SignInWithDevSsoAsync(ownerEmail, Ct);
        await ReadAsync(await ownerSession.PostAsync("/v1/auth/onboarding", new { organizationName = Organization, slug = $"test-{Guid.NewGuid():N}" }, Ct), HttpStatusCode.OK);
        var theirs = (await ReadAsync(await ownerSession.PostAsync("/v1/jobs", new { title = TheirJob }, Ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var other = (await ReadAsync(await ownerSession.PostAsync("/v1/jobs", new { title = OtherJob }, Ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var memberEmail = ApiSession.NewEmail("viewer");
        using var memberSession = await InviteAsync(ownerSession, memberEmail);
        var members = await ReadAsync(await ownerSession.Client.GetAsync("/v1/organization/members", Ct), HttpStatusCode.OK);
        var memberName = members.EnumerateArray().Single(m => m.GetProperty("email").GetString() == memberEmail).GetProperty("displayName").GetString()!;

        // The Owner adds them to one job's hiring team.
        await Page.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(Page, ownerEmail);
        await Page.GotoAsync(DashboardUrl($"/jobs/{theirs}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(TheirJob);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Hiring team") }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Hiring team" });
        await Expect(sheet.GetByRole(AriaRole.List, new() { Name = "People on the team" })).ToContainTextAsync("Owner");
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Add a person" }).ClickAsync();
        await Page.GetByPlaceholder("Search people").FillAsync(memberName.Split(' ')[0]);
        await Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex(Regex.Escape(memberName)) }).ClickAsync();
        await Expect(sheet).ToContainTextAsync("Only jobs they're on");
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(sheet).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Hiring team") })).ToContainTextAsync("2");

        // The member sees that job and not the other.
        await using var memberContext = await Fixture.Browser.NewContextAsync(new() { Locale = "en-US" });
        var memberPage = await memberContext.NewPageAsync();
        await memberPage.SetViewportSizeAsync(1440, 1000);
        await SignInAsync(memberPage, memberEmail);
        await Expect(memberPage.GetByRole(AriaRole.Link, new() { Name = TheirJob })).ToBeVisibleAsync();
        await Expect(memberPage.GetByRole(AriaRole.Link, new() { Name = OtherJob })).ToHaveCountAsync(0);
        await memberPage.GotoAsync(DashboardUrl($"/jobs/{other}"));
        await Expect(memberPage.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Job not found");

        // Team says how many jobs they see, and the access sheet lists the job, read only.
        await Page.GotoAsync(DashboardUrl("/settings/team"));
        var row = Page.GetByRole(AriaRole.Table).First.GetByRole(AriaRole.Row).Filter(new() { HasText = memberEmail });
        await Expect(row).ToContainTextAsync("1 job");
        await row.GetByRole(AriaRole.Button, new() { Name = $"Actions for {memberName}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Edit access" }).ClickAsync();
        var access = Page.GetByRole(AriaRole.Dialog, new() { Name = $"{memberName}'s access" });
        await Expect(access.GetByRole(AriaRole.Radio, new() { NameRegex = new Regex("^Only jobs they're on") })).ToBeCheckedAsync();
        await Expect(access.GetByRole(AriaRole.Link, new() { Name = TheirJob })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await access.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        // Copy from another job brings the member onto the second job.
        await Page.GotoAsync(DashboardUrl($"/jobs/{other}"));
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Hiring team") }).ClickAsync();
        sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Hiring team" });
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Copy from another job" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = TheirJob }).ClickAsync();
        await Expect(sheet.GetByRole(AriaRole.List, new() { Name = "People on the team" })).ToContainTextAsync(memberName);
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(sheet).ToHaveCountAsync(0);

        await memberPage.GotoAsync(DashboardUrl("/"));
        await Expect(memberPage.GetByRole(AriaRole.Link, new() { Name = OtherJob })).ToBeVisibleAsync();
    }

    // The Owner invites an address on Only jobs they're on; the invited person accepts the emailed link in a session of
    // their own.
    private async Task<ApiSession> InviteAsync(ApiSession owner, string email)
    {
        using (var invited = await owner.PostAsync("/v1/organization/invitations", new { email, role = "Viewer", jobAccess = "HiringTeamOnly" }, Ct))
        {
            invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Ct));
        }

        var token = await Fixture.Mailpit.WaitForInvitationTokenAsync(email, Ct);
        var session = new ApiSession(Fixture);
        try
        {
            using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token }, Ct);
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

    // Dev SSO finds the account by its address, so an invited person signs in to their organization.
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
