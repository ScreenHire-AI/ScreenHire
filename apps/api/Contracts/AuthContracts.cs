using System.Net.Mail;
using FluentValidation;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Contracts;

/// <summary>How a person can sign in here: the configured identity providers, and whether email links work.</summary>
public sealed record SignInMethods(IReadOnlyList<SignInProvider> Providers, bool MagicLinkEnabled);

/// <summary>An identity provider button on the sign-in page.</summary>
public sealed record SignInProvider(string Id, string DisplayName);

/// <summary>Asks for a sign-in link by email.</summary>
public sealed record MagicLinkRequest(string Email);

/// <summary>Redeems the token from an emailed sign-in link.</summary>
public sealed record MagicLinkVerification(string Token);

/// <summary>Creates the signed-in user's organization, with them as its Owner.</summary>
public sealed record OnboardingRequest(string OrganizationName, string Slug);

/// <summary>The signed-in user, and the organization they work in (null until onboarding).</summary>
public sealed record CurrentUser(Guid Id, string Email, string DisplayName, CurrentOrganization? Organization);

/// <summary>The organization the signed-in user works in, and their role there.</summary>
public sealed record CurrentOrganization(Guid Id, string Name, string Slug, OrganizationRole Role);

internal sealed class MagicLinkRequestValidator : AbstractValidator<MagicLinkRequest>
{
    public MagicLinkRequestValidator() =>
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).Must(IsBareAddress).WithMessage("Enter an email address, like name@example.com.");

    /// <summary>
    /// One address and nothing else. FluentValidation's <c>EmailAddress()</c> only looks for an <c>@</c>, so it lets
    /// through <c>Name &lt;victim@example.com&gt;</c> (a fresh per-address link cap for every display name) and lists of
    /// addresses, which the SMTP sender cannot parse.
    /// </summary>
    internal static bool IsBareAddress(string? email)
    {
        var trimmed = email?.Trim();
        return trimmed is { Length: > 0 }
            && MailAddress.TryCreate(trimmed, out var parsed)
            && parsed.DisplayName.Length == 0
            && string.Equals(parsed.Address, trimmed, StringComparison.Ordinal);
    }
}

internal sealed class MagicLinkVerificationValidator : AbstractValidator<MagicLinkVerification>
{
    public MagicLinkVerificationValidator() => RuleFor(r => r.Token).NotEmpty().MaximumLength(100);
}

internal sealed class OnboardingRequestValidator : AbstractValidator<OnboardingRequest>
{
    /// <summary>
    /// Slugs become careers-page subdomains (<c>{tenant}.{CareersDomain}</c>, ADR-0003), so they are DNS labels, and
    /// names the platform itself may need are reserved.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "api", "app", "apply", "assets", "auth", "blog", "careers", "cdn", "dashboard", "docs", "help", "jobs",
        "login", "mail", "public", "screenhire", "signin", "sign-in", "static", "status", "support", "www",
    };

    public OnboardingRequestValidator()
    {
        RuleFor(r => r.OrganizationName).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter your organization's name.")
            .MaximumLength(200);
        RuleFor(r => r.Slug)
            .Length(3, 63).WithMessage("Use 3 to 63 characters.")
            .Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$").WithMessage("Use lower-case letters, numbers and hyphens, not at the start or end.")
            .Must(slug => !ReservedSlugs.Contains(slug)).WithMessage("That address is reserved. Choose another.");
    }
}
