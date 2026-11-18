namespace ScreenHire.E2E;

/// <summary>
/// What the Settings classes share: a new person who owns a new organization, their sign-in through Dev SSO in the
/// browser, and the dashboard's addresses. The Settings journeys are several classes (<see cref="SettingsTests"/>,
/// <see cref="SettingsTeamTests"/>, <see cref="SettingsBrandingTests"/>, <see cref="SettingsAccessTests"/>), because CI
/// gives a whole class to one shard and one class holding them all outweighed an average shard
/// (.claude/rules/test-value.md).
/// </summary>
public abstract class SettingsArea(E2EFixture fixture) : BrowserTest(fixture)
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A new person who owns a new organization; returns their email and the organization's slug.
    protected async Task<(string Email, string Slug)> OwnerAsync()
    {
        var email = ApiSession.NewEmail("e2e-owner");
        using var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(email, Ct);
        await session.OnboardAsync(Ct);
        var me = (await session.CurrentUserAsync(Ct))!.Value;
        return (email, me.GetProperty("organization").GetProperty("slug").GetString()!);
    }

    protected async Task SignInAsync(string email)
    {
        var response = await Page.GotoAsync(DashboardUrl("/sign-in"));
        response!.Ok.Should().BeTrue();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    protected string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();
}
