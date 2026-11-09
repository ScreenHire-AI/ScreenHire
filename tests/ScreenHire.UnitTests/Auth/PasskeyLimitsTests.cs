using ScreenHire.Api.Contracts;

namespace ScreenHire.UnitTests.Auth;

public sealed class PasskeyLimitsTests
{
    [Theory]
    [InlineData(1, 0, false, false)] // the last passkey, and nothing else signs this person in
    [InlineData(1, 1, false, true)] // an identity provider that is still configured
    [InlineData(1, 0, true, true)] // emailed links work
    [InlineData(2, 0, false, true)] // another passkey remains
    [InlineData(3, 2, true, true)]
    public void A_person_keeps_a_way_to_sign_in(int passkeys, int usableLogins, bool emailLinks, bool mayRemove) =>
        PasskeyLimits.MayRemove(passkeys, usableLogins, emailLinks).Should().Be(mayRemove);

    [Theory]
    [InlineData(null, "Passkey")]
    [InlineData("", "Passkey")]
    [InlineData("   ", "Passkey")]
    [InlineData("\u0007\n", "Passkey")]
    [InlineData("  Work laptop ", "Work laptop")]
    [InlineData("Phone\u0000\u0007", "Phone")]
    public void A_name_is_trimmed_and_stripped_of_control_characters(string? name, string expected) =>
        PasskeyLimits.CleanName(name).Should().Be(expected);

    [Fact]
    public void A_name_may_be_sixty_characters_and_a_credential_must_not_be_empty()
    {
        var validator = new RegisterPasskeyRequestValidator();

        validator.Validate(new RegisterPasskeyRequest("{}", new string('a', PasskeyLimits.NameLength))).IsValid.Should().BeTrue();
        validator.Validate(new RegisterPasskeyRequest("{}", new string('a', PasskeyLimits.NameLength + 1))).Errors
            .Select(e => e.PropertyName).Should().Equal(nameof(RegisterPasskeyRequest.Name));
        validator.Validate(new RegisterPasskeyRequest("")).Errors
            .Select(e => e.PropertyName).Should().Contain(nameof(RegisterPasskeyRequest.CredentialJson));
        new PasskeySignInRequestValidator().Validate(new PasskeySignInRequest(new string('x', PasskeyLimits.CredentialJsonLength + 1))).IsValid
            .Should().BeFalse();
    }
}
