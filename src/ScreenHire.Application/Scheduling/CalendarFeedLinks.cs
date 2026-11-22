using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace ScreenHire.Application.Scheduling;

/// <summary>What a valid subscribe link names: a member of an organization and the feed secret it was issued under.</summary>
public sealed record CalendarFeedLink(Guid OrganizationId, Guid MemberId, string Secret);

/// <summary>
/// The private subscribe links of ScreenHire's calendar (docs/architecture/interview-scheduling.md, "The subscribe link"): a
/// member's booked interviews as an iCalendar feed a phone's calendar app reads on its own, with no sign-in. The token is the
/// organization, the member and the feed secret on their <c>InterviewHours</c>, protected with Data Protection under its own
/// purpose. It has no time limit, because a calendar subscription lives as long as the person keeps it; a new secret (Reset
/// link) or none (the link turned off) ends every token issued before, so the secret is what closes it.
/// </summary>
public sealed class CalendarFeedLinks(IDataProtectionProvider dataProtection)
{
    public const string Purpose = "ScreenHire.Calendar.Feed.v1";

    private readonly IDataProtector protector = dataProtection.CreateProtector(Purpose);

    /// <summary>A new feed secret: 32 random bytes as lowercase hex.</summary>
    public static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public string Issue(Guid organizationId, Guid memberId, string secret) =>
        protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{organizationId:N}.{memberId:N}.{secret}"));

    /// <summary>What <paramref name="token"/> names, or null when it is forged or damaged.</summary>
    public CalendarFeedLink? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 1000)
        {
            return null;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('.');
        return parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out var organization) && Guid.TryParseExact(parts[1], "N", out var member) && parts[2].Length > 0
            ? new CalendarFeedLink(organization, member, parts[2])
            : null;
    }

    /// <summary>Whether a link's secret is the member's current one, compared in constant time.</summary>
    public static bool Matches(string? current, string presented) =>
        current is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(presented));
}
