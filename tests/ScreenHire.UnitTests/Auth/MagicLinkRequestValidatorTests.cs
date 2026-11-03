using ScreenHire.Api.Contracts;

namespace ScreenHire.UnitTests.Auth;

public sealed class MagicLinkRequestValidatorTests
{
    private readonly MagicLinkRequestValidator validator = new();

    [Theory]
    [InlineData("avery@example.com")]
    [InlineData("Avery.Quinn+jobs@mail.example.com")]
    [InlineData("  avery@example.com  ")] // surrounding spaces are trimmed before the link is sent
    public void A_single_address_is_accepted(string email) =>
        validator.Validate(new MagicLinkRequest(email)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("avery")]
    [InlineData("avery@")]
    [InlineData("@example.com")]
    [InlineData("Mallory <avery@example.com>")] // a display name would get its own per-address link cap
    [InlineData("<avery@example.com>")]
    [InlineData("avery@example.com, mallory@example.org")]
    [InlineData("avery@example.com mallory@example.org")]
    public void Anything_but_one_bare_address_is_refused(string email) =>
        validator.Validate(new MagicLinkRequest(email)).Errors
            .Should().Contain(e => e.PropertyName == nameof(MagicLinkRequest.Email));
}
