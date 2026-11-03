using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>What a valid interview link names: an organization and one of its interview requests.</summary>
public sealed record InterviewLink(Guid OrganizationId, Guid InterviewId);

/// <summary>
/// The signed links to the careers page where a candidate picks, moves or cancels an interview
/// (docs/architecture/interview-scheduling.md). A link's token is the organization and the interview request, protected
/// with ASP.NET Core Data Protection under its own purpose and time-limited, as <see cref="QuestionnaireLinks"/> are. The
/// worker issues the token when it emails the invitation; the API reads it when the careers page asks. One token serves the
/// whole life of the request (pick, move, cancel), so it is not single use: the request's status is what closes it.
/// </summary>
/// <remarks>
/// A token reads until the request's window ends plus <see cref="Grace"/>, so a page opened after the window can say the
/// link expired rather than look forged. The limit is checked by Data Protection against the system clock.
/// </remarks>
public sealed class InterviewLinks(IDataProtectionProvider dataProtection, IOptions<CareersSiteOptions> careers)
{
    public const string Purpose = "ScreenHire.Interview.Link.v1";

    /// <summary>How long after the window ends a token still reads.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private readonly ITimeLimitedDataProtector protector = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>The token for <paramref name="interview"/>, valid until its window ends plus <see cref="Grace"/>.</summary>
    public string Issue(InterviewRequest interview)
    {
        ArgumentNullException.ThrowIfNull(interview);
        return protector.Protect(
            string.Create(CultureInfo.InvariantCulture, $"{interview.OrganizationId:N}.{interview.Id:N}"),
            interview.WindowEnd + Grace);
    }

    /// <summary>False without a careers host pattern (<c>ScreenHire:Domains</c>): a link would name no host.</summary>
    public bool IsConfigured => careers.Value.CareersHostPattern.Contains("{tenant}", StringComparison.Ordinal);

    /// <summary>The careers page for <paramref name="token"/> on the organization's careers board.</summary>
    public string Url(string organizationSlug, string token) => $"{careers.Value.BoardUrl(organizationSlug)}/interviews/{token}";

    /// <summary>What <paramref name="token"/> names, or null when it is forged, damaged or past its own limit.</summary>
    public InterviewLink? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 1000)
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
            && Guid.TryParseExact(parts[1], "N", out var interview)
                ? new InterviewLink(organization, interview)
                : null;
    }
}
