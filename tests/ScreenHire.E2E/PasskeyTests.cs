namespace ScreenHire.E2E;

/// <summary>
/// Signing in with a passkey in a real browser. Chromium's virtual authenticator (the DevTools <c>WebAuthn</c> domain) plays
/// the device, so the page's own WebAuthn calls, the dashboard's <c>/v1</c> proxy and the API's relying party and origin
/// checks all run for real. It works headless: the virtual authenticator needs no operating system prompt, and
/// <c>localhost</c> is a secure context.
/// </summary>
public sealed class PasskeyTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_adds_a_passkey_signs_out_signs_in_with_it_and_removes_it()
    {
        var email = ApiSession.NewEmail("e2e-passkey");
        using (var session = new ApiSession(Fixture))
        {
            await session.SignInWithDevSsoAsync(email, Ct);
            await session.OnboardAsync(Ct);
        }

        await AddVirtualAuthenticatorAsync();
        await SignInWithDevSsoAsync(email);

        // Add one: the person's menu, the Passkeys page, a name for this device.
        await OpenPasskeysPageAsync(email);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Passkeys");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add a passkey" }).First.ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await dialog.GetByLabel("Name").FillAsync("Test laptop");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Add passkey" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row, new() { Name = "Test laptop" })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        // Sign out, then sign in with the passkey alone: no provider, no email.
        await Page.GetByRole(AriaRole.Button, new() { Name = email }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/sign-in"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");

        // It now says when it was last used, and removing it leaves the page empty.
        await OpenPasskeysPageAsync(email);
        var row = Page.GetByRole(AriaRole.Row, new() { Name = "Test laptop" });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).Not.ToContainTextAsync("Never used");
        await row.GetByRole(AriaRole.Button, new() { Name = "Actions for Test laptop" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Remove passkey" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Remove passkey" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Row, new() { Name = "Test laptop" })).ToHaveCountAsync(0);
    }

    // A platform authenticator that remembers its passkey, verifies the person and answers at once, as a laptop with
    // Windows Hello or Touch ID does.
    private async Task AddVirtualAuthenticatorAsync()
    {
        var cdp = await Page.Context.NewCDPSessionAsync(Page);
        await cdp.SendAsync("WebAuthn.enable", new Dictionary<string, object> { ["enableUI"] = false });
        await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2",
                ["transport"] = "internal",
                ["hasResidentKey"] = true,
                ["hasUserVerification"] = true,
                ["isUserVerified"] = true,
                ["automaticPresenceSimulation"] = true,
            },
        });
    }

    private async Task SignInWithDevSsoAsync(string email)
    {
        await Page.GotoAsync(DashboardUrl("/sign-in"));
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private async Task OpenPasskeysPageAsync(string email)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = email }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Passkeys" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(DashboardUrl("/account/passkeys"));
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
