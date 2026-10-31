using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;

namespace ScreenHire.Application.TalentPool;

/// <summary>What a valid link to stop being kept names: an organization and one of its candidates.</summary>
public sealed record TalentPoolLink(Guid OrganizationId, Guid CandidateId);

/// <summary>
/// The links in a talent pool invitation (docs/architecture/talent-pool.md): the job's careers page, which is public and
/// carries nothing about the candidate, and the signed link with which the candidate stops being kept. That token is the
/// organization and the candidate, protected with ASP.NET Core Data Protection under its own purpose, like
/// <see cref="QuestionnaireLinks"/>. The worker issues it when it sends an invitation; the API reads it when the careers
/// page asks.
/// </summary>
/// <remarks>
/// The token never expires: stopping should always work, from any invitation, and all it can do is withdraw the
/// candidate's permission. It goes to the candidate alone; the copy the team reads in Messages never holds it.
/// </remarks>
public sealed class TalentPoolLinks(IDataProtectionProvider dataProtection, IOptions<CareersSiteOptions> careers)
{
    private readonly IDataProtector protector = dataProtection.CreateProtector("ScreenHire.TalentPool.Remove.v1");

    /// <summary>False without a careers host pattern (<c>ScreenHire:Domains</c>): a link would name no host.</summary>
    public bool IsConfigured => careers.Value.CareersHostPattern.Contains("{tenant}", StringComparison.Ordinal);

    /// <summary>The token for <paramref name="candidateId"/> in <paramref name="organizationId"/>.</summary>
    public string Issue(Guid organizationId, Guid candidateId) =>
        protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{organizationId:N}.{candidateId:N}"));

    /// <summary>The careers page where the candidate of <paramref name="token"/> stops being kept.</summary>
    public string RemoveUrl(string organizationSlug, string token) => $"{careers.Value.BoardUrl(organizationSlug)}/talent-pool/{token}";

    /// <summary>A job's public careers page, as the job board and Google link to it.</summary>
    public string JobUrl(string organizationSlug, string jobSlug) => $"{careers.Value.BoardUrl(organizationSlug)}/jobs/{jobSlug}";

    /// <summary>What <paramref name="token"/> names, or null when it is forged or damaged.</summary>
    public TalentPoolLink? Read(string? token)
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
        return parts.Length == 2
            && Guid.TryParseExact(parts[0], "N", out var organization)
            && Guid.TryParseExact(parts[1], "N", out var candidate)
                ? new TalentPoolLink(organization, candidate)
                : null;
    }
}
