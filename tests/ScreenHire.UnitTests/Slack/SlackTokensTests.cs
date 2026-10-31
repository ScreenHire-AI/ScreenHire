using Microsoft.AspNetCore.DataProtection;
using ScreenHire.Application.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>The bot token at rest: protected under the organization's own purpose, so a copied value does not read.</summary>
public sealed class SlackTokensTests
{
    private readonly SlackTokens tokens = new(new EphemeralDataProtectionProvider());
    private readonly Guid organization = Guid.CreateVersion7();

    [Fact]
    public void A_token_round_trips_for_its_organization_and_is_not_stored_in_the_clear()
    {
        var protectedToken = tokens.Protect(organization, "xoxb-fictional-token");

        protectedToken.Should().NotContain("xoxb");
        tokens.Unprotect(organization, protectedToken).Should().Be("xoxb-fictional-token");
    }

    [Fact]
    public void Another_organizations_id_cannot_read_the_value()
    {
        var protectedToken = tokens.Protect(organization, "xoxb-fictional-token");

        tokens.Unprotect(Guid.CreateVersion7(), protectedToken).Should().BeNull();
    }

    [Fact]
    public void A_tampered_value_does_not_read()
    {
        var protectedToken = tokens.Protect(organization, "xoxb-fictional-token");
        var tampered = protectedToken[..^4] + (protectedToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        tokens.Unprotect(organization, tampered).Should().BeNull();
        tokens.Unprotect(organization, "not a protected value").Should().BeNull();
    }
}
