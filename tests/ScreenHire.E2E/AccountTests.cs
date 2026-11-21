using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// Your account in a real browser (design system section 21): the person's menu holds only their name and email, Your
/// account, Keyboard shortcuts, Billing (an Owner, with the sandbox payment provider on) and Sign out; Your account opens Profile with its own menu; and a choice saved in the
/// notification grid is what the page shows after a reload.
/// </summary>
public sealed class AccountTests(E2EFixture fixture) : SettingsArea(fixture)
{
    [Fact]
    public async Task The_menu_opens_Your_account_and_a_notification_choice_is_saved()
    {
        var (email, _) = await OwnerAsync();
        await SignInAsync(email);

        // The menu is short: who is signed in, then the person's own things.
        await Page.GetByRole(AriaRole.Button, new() { Name = ApiSession.OwnerName }).ClickAsync();
        var menu = Page.GetByRole(AriaRole.Menu);
        await Expect(menu).ToContainTextAsync(email);
        await Expect(menu.GetByRole(AriaRole.Menuitem)).ToHaveTextAsync([new Regex("^Your account$"), new Regex("^Keyboard shortcuts"), new Regex("^Billing$"), new Regex("^Sign out$")]);
        await menu.GetByRole(AriaRole.Menuitem, new() { Name = "Your account" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/account/profile$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Profile");
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Email" })).ToHaveValueAsync(email);
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Your account" }).GetByRole(AriaRole.Link, new() { Name = "Notifications" }).ClickAsync();
        var mentions = Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Combobox, new() { Name = "Mentions: email" });
        await Expect(mentions).ToHaveValueAsync("Instantly");
        await mentions.SelectOptionAsync("Digest");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Saved." })).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Combobox, new() { Name = "Mentions: email" })).ToHaveValueAsync("Digest");
    }
}
