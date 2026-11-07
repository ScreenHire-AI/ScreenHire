using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;

namespace ScreenHire.Application.Portal;

/// <summary><c>ScreenHire:Portal</c>: the candidate portal's link and replies (docs/architecture/candidate-portal.md).</summary>
public sealed class PortalOptions
{
    public const string Section = "ScreenHire:Portal";

    /// <summary>The rate limiter policy on the public portal routes, partitioned by the visitor's address (<see cref="PermitsPerMinute"/>).</summary>
    public const string RateLimitPolicy = "portal";

    /// <summary>
    /// How long a portal link works, whether it was emailed on request or sits at the end of an email about an application.
    /// Every new email carries a fresh one, so a candidate who is still in touch always has a working link.
    /// </summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The least time between two sign-in emails to one address, so the form cannot be used to flood an inbox.</summary>
    public TimeSpan LinkCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many requests to the public portal routes one address may make in a minute.</summary>
    public int PermitsPerMinute { get; set; } = 20;

    /// <summary>The most characters a reply written on the page may hold.</summary>
    public int ReplyBodyMax { get; set; } = 5000;

    /// <summary>The most replies one application takes from its page in a day, so a leaked link cannot be used to flood the team's notifications.</summary>
    public int RepliesPerDay { get; set; } = 10;
}

/// <summary>What a valid portal link names: an organization and one of its candidates.</summary>
public sealed record CandidatePortalLink(Guid OrganizationId, Guid CandidateId);

/// <summary>
/// The signed link to a candidate's page. A token is the organization and the candidate, protected with ASP.NET Core Data
/// Protection under its own purpose and limited in time, as the data request links are (<see cref="Privacy.DataRequestLinks"/>). It is not stored: the
/// worker issues one whenever it mails the candidate, so a token never sits in the outbox's message table, and the API
/// reads it when the careers page asks. The expiry is inside the token, so an expired link is refused like a forged one.
/// </summary>
public sealed class CandidatePortalLinks(
    IDataProtectionProvider dataProtection, IOptions<CareersSiteOptions> careers, IOptions<PortalOptions> options, TimeProvider time)
{
    private const int MaxTokenLength = 1000;

    private readonly ITimeLimitedDataProtector protector =
        dataProtection.CreateProtector("ScreenHire.CandidatePortal.Link.v1").ToTimeLimitedDataProtector();

    /// <summary>False without a careers host pattern (<c>ScreenHire:Domains</c>): a link would name no host.</summary>
    public bool IsConfigured => careers.Value.CareersHostPattern.Contains("{tenant}", StringComparison.Ordinal);

    /// <summary>How long a token from <see cref="Issue"/> works, for an email to say so.</summary>
    public TimeSpan LinkLifetime => options.Value.LinkLifetime;

    /// <summary>The token that opens <paramref name="candidateId"/>'s page, valid for <see cref="PortalOptions.LinkLifetime"/>.</summary>
    public string Issue(Guid organizationId, Guid candidateId) =>
        protector.Protect(
            string.Create(CultureInfo.InvariantCulture, $"{organizationId:N}.{candidateId:N}"),
            time.GetUtcNow() + options.Value.LinkLifetime);

    /// <summary>The careers page for <paramref name="token"/> on the organization's careers board.</summary>
    public string Url(string organizationSlug, string token) => $"{careers.Value.BoardUrl(organizationSlug)}/my-applications/{token}";

    /// <summary>The link to put in an email to the candidate: the page's address, or null where no careers host is configured.</summary>
    public string? UrlFor(string organizationSlug, Guid organizationId, Guid candidateId) =>
        IsConfigured ? Url(organizationSlug, Issue(organizationId, candidateId)) : null;

    /// <summary>What <paramref name="token"/> names, or null when it is forged, damaged, expired or made for another purpose.</summary>
    public CandidatePortalLink? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return null;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token, out _);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('.');
        return parts.Length == 2
            && Guid.TryParseExact(parts[0], "N", out var organization)
            && Guid.TryParseExact(parts[1], "N", out var candidate)
                ? new CandidatePortalLink(organization, candidate)
                : null;
    }
}
