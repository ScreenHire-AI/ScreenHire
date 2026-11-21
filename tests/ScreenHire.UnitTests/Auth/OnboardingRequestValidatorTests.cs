using ScreenHire.Api.Contracts;

namespace ScreenHire.UnitTests.Auth;

public sealed class OnboardingRequestValidatorTests
{
    private readonly OnboardingRequestValidator validator = new();

    [Theory]
    [InlineData("acme")]
    [InlineData("acme-hiring-2026")]
    [InlineData("a1b")]
    public void Dns_label_slugs_are_accepted(string slug) =>
        validator.Validate(new OnboardingRequest("Acme", slug, "Casey Morgan")).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("ab")] // too short
    [InlineData("Acme")] // upper case
    [InlineData("acme hiring")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme_hiring")]
    [InlineData("www")] // reserved
    [InlineData("api")]
    public void Other_slugs_are_refused(string slug)
    {
        var result = validator.Validate(new OnboardingRequest("Acme", slug, "Casey Morgan"));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(OnboardingRequest.Slug));
    }

    [Fact]
    public void Slug_is_at_most_63_characters()
    {
        validator.Validate(new OnboardingRequest("Acme", new string('a', 63), "Casey Morgan")).IsValid.Should().BeTrue();
        validator.Validate(new OnboardingRequest("Acme", new string('a', 64), "Casey Morgan")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Organization_needs_a_name(string name) =>
        validator.Validate(new OnboardingRequest(name, "acme", "Casey Morgan")).Errors
            .Should().Contain(e => e.PropertyName == nameof(OnboardingRequest.OrganizationName));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_person_needs_a_name(string name) =>
        validator.Validate(new OnboardingRequest("Acme", "acme", name)).Errors
            .Should().ContainSingle(e => e.PropertyName == nameof(OnboardingRequest.Name) && e.ErrorMessage == "Enter your name.");

    [Fact]
    public void A_name_is_at_most_200_characters()
    {
        validator.Validate(new OnboardingRequest("Acme", "acme", new string('a', 200))).IsValid.Should().BeTrue();
        validator.Validate(new OnboardingRequest("Acme", "acme", new string('a', 201))).IsValid.Should().BeFalse();
    }
}
