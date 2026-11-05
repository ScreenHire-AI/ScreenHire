using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;

namespace ScreenHire.Application.Privacy;

/// <summary>What a valid link to a candidate's data request choices names: an organization and one of its candidates.</summary>
public sealed record DataRequestLink(Guid OrganizationId, Guid CandidateId);

/// <summary>What a valid link to download a copy of a candidate's data names: an organization and one of its data requests.</summary>
public sealed record DataRequestDownload(Guid OrganizationId, Guid RequestId);

/// <summary>
/// The two signed links of the data request flow (docs/architecture/data-requests.md): the emailed link that opens the
/// choices (a copy, or erasure) and the link in the email that carries a copy. Each token is an organization and an id,
/// protected with ASP.NET Core Data Protection under its own purpose and limited in time, as
/// <see cref="QuestionnaireLinks"/> are. The worker issues both when it sends the email, so a token never sits in the
/// outbox's message table; the API reads them when the careers page asks.
/// </summary>
/// <remarks>
/// Both tokens carry their expiry inside, so an expired token is refused like a forged one and the page cannot tell
/// them apart. The download token's limit is the copy's own (<see cref="DataRequestOptions.ExportLifetime"/>); the request
/// row's <c>ExportExpiresAt</c> is checked as well, because the copy is deleted when it passes.
/// </remarks>
public sealed class DataRequestLinks(
    IDataProtectionProvider dataProtection, IOptions<CareersSiteOptions> careers, IOptions<DataRequestOptions> options, TimeProvider time)
{
    private const int MaxTokenLength = 1000;

    private readonly ITimeLimitedDataProtector linkProtector =
        dataProtection.CreateProtector("ScreenHire.DataRequest.Link.v1").ToTimeLimitedDataProtector();

    private readonly ITimeLimitedDataProtector downloadProtector =
        dataProtection.CreateProtector("ScreenHire.DataRequest.Download.v1").ToTimeLimitedDataProtector();

    /// <summary>False without a careers host pattern (<c>ScreenHire:Domains</c>): a link would name no host.</summary>
    public bool IsConfigured => careers.Value.CareersHostPattern.Contains("{tenant}", StringComparison.Ordinal);

    /// <summary>How long a token from <see cref="IssueLink"/> works, for the email to say so.</summary>
    public TimeSpan LinkLifetime => options.Value.LinkLifetime;

    /// <summary>The token that opens the choices for <paramref name="candidateId"/>, valid for <see cref="DataRequestOptions.LinkLifetime"/>.</summary>
    public string IssueLink(Guid organizationId, Guid candidateId) =>
        linkProtector.Protect(Payload(organizationId, candidateId), time.GetUtcNow() + options.Value.LinkLifetime);

    /// <summary>The token that downloads the copy for <paramref name="requestId"/>, valid for <see cref="DataRequestOptions.ExportLifetime"/>.</summary>
    public string IssueDownload(Guid organizationId, Guid requestId) =>
        downloadProtector.Protect(Payload(organizationId, requestId), time.GetUtcNow() + options.Value.ExportLifetime);

    /// <summary>The careers page for <paramref name="token"/> on the organization's careers board.</summary>
    public string LinkUrl(string organizationSlug, string token) => $"{careers.Value.BoardUrl(organizationSlug)}/data-request/{token}";

    /// <summary>The careers address that sends the candidate to their copy.</summary>
    public string DownloadUrl(string organizationSlug, string token) => $"{careers.Value.BoardUrl(organizationSlug)}/data-request/download/{token}";

    /// <summary>What <paramref name="token"/> names, or null when it is forged, damaged, expired or made for another purpose.</summary>
    public DataRequestLink? ReadLink(string? token) =>
        Read(linkProtector, token) is { } found ? new DataRequestLink(found.Organization, found.Id) : null;

    /// <summary>What <paramref name="token"/> names, or null when it is forged, damaged, expired or made for another purpose.</summary>
    public DataRequestDownload? ReadDownload(string? token) =>
        Read(downloadProtector, token) is { } found ? new DataRequestDownload(found.Organization, found.Id) : null;

    private static string Payload(Guid organizationId, Guid id) => string.Create(CultureInfo.InvariantCulture, $"{organizationId:N}.{id:N}");

    private static (Guid Organization, Guid Id)? Read(ITimeLimitedDataProtector protector, string? token)
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
            && Guid.TryParseExact(parts[1], "N", out var id)
                ? (organization, id)
                : null;
    }
}
