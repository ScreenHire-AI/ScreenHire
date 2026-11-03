using Microsoft.AspNetCore.DataProtection;
using ScreenHire.Application.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>A member's calendar tokens at rest: protected under the organization's and the member's own purpose, so a copied value does not read.</summary>
public sealed class CalendarTokensTests
{
    private readonly CalendarTokens tokens = new(new EphemeralDataProtectionProvider());
    private readonly Guid organization = Guid.CreateVersion7();
    private readonly Guid user = Guid.CreateVersion7();

    [Fact]
    public void A_token_round_trips_for_its_member_and_is_not_stored_in_the_clear()
    {
        var protectedToken = tokens.Protect(organization, user, "ya29.fictional-token");

        protectedToken.Should().NotContain("ya29");
        tokens.Unprotect(organization, user, protectedToken).Should().Be("ya29.fictional-token");
    }

    [Fact]
    public void Another_member_of_the_same_organization_cannot_read_the_value()
    {
        var protectedToken = tokens.Protect(organization, user, "ya29.fictional-token");

        tokens.Unprotect(organization, Guid.CreateVersion7(), protectedToken).Should().BeNull();
    }

    [Fact]
    public void Another_organizations_id_cannot_read_the_value()
    {
        var protectedToken = tokens.Protect(organization, user, "ya29.fictional-token");

        tokens.Unprotect(Guid.CreateVersion7(), user, protectedToken).Should().BeNull();
    }

    [Fact]
    public void A_tampered_value_does_not_read()
    {
        var protectedToken = tokens.Protect(organization, user, "ya29.fictional-token");
        var tampered = protectedToken[..^4] + (protectedToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        tokens.Unprotect(organization, user, tampered).Should().BeNull();
        tokens.Unprotect(organization, user, "not a protected value").Should().BeNull();
    }
}
