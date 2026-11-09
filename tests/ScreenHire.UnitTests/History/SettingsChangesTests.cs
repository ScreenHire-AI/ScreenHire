using ScreenHire.Domain.History;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// The settings pages' history lines (change-history.md): the organization's name, its brand colour and its verification
/// settings, each only when it changed.
/// </summary>
public sealed class SettingsChangesTests
{
    [Fact]
    public void A_rename_is_the_names_history_and_nothing_in_the_brand_colours()
    {
        var before = Organization("Brightwater");
        var after = Organization("Brightwater Studio");

        Sentences(SettingsChanges.Name(before, after)).Should().Equal("renamed the organization from Brightwater to Brightwater Studio");
        SettingsChanges.Branding(before, after).Should().BeEmpty();
        SettingsChanges.Name(null, after).Should().BeEmpty("making the organization is not a change on its settings page");
    }

    [Fact]
    public void Each_site_that_may_embed_the_careers_page_has_its_own_line_on_the_organizations_history()
    {
        var none = Organization("Brightwater");
        var one = Organization("Brightwater");
        one.SetEmbedOrigins(["https://www.example.com"], allowLoopback: false);
        var other = Organization("Brightwater");
        other.SetEmbedOrigins(["https://jobs.example.com"], allowLoopback: false);

        Sentences(SettingsChanges.Name(none, one)).Should().Equal("let https://www.example.com embed the careers page");
        Sentences(SettingsChanges.Name(one, other)).Should().Equal(
            "let https://jobs.example.com embed the careers page", "stopped https://www.example.com embedding the careers page");
        SettingsChanges.Name(one, one).Should().BeEmpty();
        SettingsChanges.Branding(none, one).Should().BeEmpty();
    }

    [Fact]
    public void The_brand_colour_says_whether_it_was_set_changed_or_put_back_to_the_default()
    {
        var plain = Organization("Brightwater");
        var teal = Organization("Brightwater", "#1f4e5a");
        var indigo = Organization("Brightwater", "#2b2788");

        Sentences(SettingsChanges.Branding(plain, teal)).Should().Equal("set the brand colour to #1f4e5a");
        Sentences(SettingsChanges.Branding(teal, indigo)).Should().Equal("changed the brand colour from #1f4e5a to #2b2788");
        Sentences(SettingsChanges.Branding(indigo, plain)).Should().Equal("went back to ScreenHire's default colour");
        SettingsChanges.Name(teal, indigo).Should().BeEmpty();
    }

    [Fact]
    public void The_branding_names_each_part_that_changed_in_one_save()
    {
        var plain = Organization("Brightwater");
        var logo = Guid.NewGuid();
        var branded = Organization("Brightwater");
        branded.SetBranding(null, "#e9dcc4", HeadingFont.PlayfairDisplay, logo, null, null);
        var rebranded = Organization("Brightwater");
        rebranded.SetBranding(null, "#1f4e5a", null, Guid.NewGuid(), Guid.NewGuid(), null);

        Sentences(SettingsChanges.Branding(plain, branded)).Should().Equal(
            "set the second colour to #e9dcc4", "changed the heading font to Playfair Display", "added a logo");
        Sentences(SettingsChanges.Branding(branded, rebranded)).Should().Equal(
            "changed the second colour from #e9dcc4 to #1f4e5a", "went back to ScreenHire's heading font", "replaced the logo", "added a favicon");
        Sentences(SettingsChanges.Branding(rebranded, plain)).Should().Equal("removed the second colour", "removed the logo", "removed the favicon");
        SettingsChanges.Name(plain, branded).Should().BeEmpty();
    }

    [Fact]
    public void The_verification_settings_name_each_change_and_compare_a_first_save_with_the_defaults()
    {
        var on = new VerificationSettings { LlmConsistencyCheckEnabled = true, MonthlyBudget = 250_000 };
        var off = new VerificationSettings { LlmConsistencyCheckEnabled = false, MonthlyBudget = null };

        Sentences(SettingsChanges.Verification(null, on)).Should().Equal("turned on the consistency check", "set the monthly budget to 250,000 tokens");
        Sentences(SettingsChanges.Verification(on, off)).Should().Equal("turned off the consistency check", "removed the monthly budget");
        SettingsChanges.Verification(null, off).Should().BeEmpty("saving the defaults changes nothing");
    }

    [Fact]
    public void The_monthly_budget_and_cap_are_named_in_the_unit_they_are_counted_in_and_the_own_keys_prices_are_never_shown()
    {
        var tokens = new VerificationSettings { MonthlyBudget = 1_000_000, MonthlyCap = 2_000_000 };
        var dollars = new VerificationSettings { BudgetUnit = UsageUnit.Money, MonthlyBudget = 50m, MonthlyCap = 120.5m };
        var priced = new VerificationSettings { OwnKeyInputPrice = 3m, OwnKeyOutputPrice = 15m };

        Sentences(SettingsChanges.Verification(null, tokens)).Should().Equal("set the monthly budget to 1,000,000 tokens", "set the monthly cap to 2,000,000 tokens");
        Sentences(SettingsChanges.Verification(tokens, dollars)).Should().Equal("set the monthly budget to $50.00", "set the monthly cap to $120.50");
        Sentences(SettingsChanges.Verification(new VerificationSettings { BudgetUnit = UsageUnit.Money }, new VerificationSettings())).Should().Equal("counts AI usage in tokens");
        Sentences(SettingsChanges.Verification(null, priced)).Should().Equal("changed the price entered for the organization's own key");
    }

    [Fact]
    public void The_verification_settings_name_the_model_the_groups_the_policy_the_trusted_entries_and_the_alerts_without_ever_showing_the_key()
    {
        var before = new VerificationSettings { TrustedDomains = ["old.example.com"], TrustedNetworks = ["203.0.113.0/24"] };
        var after = new VerificationSettings
        {
            LlmSource = LlmSource.OwnKey,
            LlmProvider = ModelProvider.OpenAiCompatible,
            LlmBaseUrl = "https://acme.example.com/secret-path-token/v1/",
            LlmModel = "model-one",
            ProtectedLlmApiKey = "CfDJ8-protected-key-text",
            LlmKeyVersion = 1,
            CheckDocuments = false,
            CheckFormUse = false,
            DefaultAiUse = ScreenHire.Domain.Hiring.AiUsePolicy.NotAllowed,
            DeviceCheck = DeviceCheckBasis.Consent,
            TrustedDomains = ["partner.example.com"],
            TrustedNetworks = [],
            SuggestedAlertEmail = true,
            BudgetAlertSlack = true,
        };

        var lines = Sentences(SettingsChanges.Verification(before, after)).ToList();

        lines.Should().Equal(
            "switched to your own AI key",
            "set the AI provider to Other (OpenAI-compatible)",
            "set the AI endpoint to acme.example.com",
            "set the AI model to model-one",
            "saved an API key",
            "turned off documents checks",
            "turned off form use checks",
            "set the AI use policy to Not allowed",
            "set the device check to Ask for consent",
            "trusted the domain partner.example.com",
            "stopped trusting the domain old.example.com",
            "stopped trusting the network 203.0.113.0/24",
            "turned on the Verification suggested alert by email",
            "turned on the budget alert on Slack");
        string.Join(' ', lines).Should().NotContain("CfDJ8").And.NotContain("secret-path-token", "an address can carry a token in its path, so History names the host alone");
    }

    [Fact]
    public void Replacing_or_removing_the_key_is_a_line_and_changing_nothing_else_about_it_is_not()
    {
        var saved = new VerificationSettings { LlmSource = LlmSource.OwnKey, LlmProvider = ModelProvider.Anthropic, LlmModel = "m", ProtectedLlmApiKey = "a", LlmKeyVersion = 1 };
        var replaced = new VerificationSettings { LlmSource = LlmSource.OwnKey, LlmProvider = ModelProvider.Anthropic, LlmModel = "m", ProtectedLlmApiKey = "b", LlmKeyVersion = 2 };
        var removed = new VerificationSettings();

        Sentences(SettingsChanges.Verification(saved, replaced)).Should().Equal("replaced the API key");
        Sentences(SettingsChanges.Verification(replaced, removed)).Should().Equal("switched to ScreenHire's included AI model", "removed the API key");
        SettingsChanges.Verification(saved, saved).Should().BeEmpty();
    }

    [Fact]
    public void The_sending_domain_says_when_it_is_added_verified_no_longer_verified_and_removed_and_nothing_for_a_check_that_found_the_same()
    {
        var pending = new SendingDomain { Domain = "acme.example.org" };
        var verified = new SendingDomain { Domain = "acme.example.org" };
        var now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        verified.RecordCheck([], providerConfirmed: true, providerMessage: null, now);
        var stillPending = new SendingDomain { Domain = "acme.example.org" };
        stillPending.RecordCheck([], providerConfirmed: false, providerMessage: "Not yet.", now);

        Sentences(SettingsChanges.SendingDomain(null, pending)).Should().Equal("added the sending domain acme.example.org");
        Sentences(SettingsChanges.SendingDomain(pending, verified)).Should().Equal("verified the sending domain acme.example.org: email now comes from it");
        Sentences(SettingsChanges.SendingDomain(verified, pending)).Should().Equal(
            "found the sending domain acme.example.org no longer verified: email comes from ScreenHire again");
        Sentences(SettingsChanges.SendingDomain(verified, null)).Should().Equal("removed the sending domain acme.example.org");
        SettingsChanges.SendingDomain(pending, stillPending).Should().BeEmpty("a check that found it still waiting is not a change anyone made");
        SettingsChanges.SendingDomain(verified, verified).Should().BeEmpty();
        SettingsChanges.SendingDomain(null, null).Should().BeEmpty();
    }

    private static Organization Organization(string name, string? brandColor = null)
    {
        var organization = new Organization { Name = name, Slug = "brightwater", HomeRegion = "eu" };
        organization.SetBrandColor(brandColor);
        return organization;
    }

    private static IEnumerable<string> Sentences(IReadOnlyList<ChangeLine> lines) => lines.Select(line => line.Sentence());
}
