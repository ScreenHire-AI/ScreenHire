using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Application.Applications;

/// <summary>
/// Where tenant careers pages live, from the same <c>ScreenHire:Domains</c> settings the API and the careers server read
/// (ADR-0003): the worker needs them to put a careers link in an email. Nothing here names a host.
/// </summary>
public sealed class CareersSiteOptions
{
    public const string Section = "ScreenHire:Domains";

    /// <summary><c>https</c> everywhere except local development.</summary>
    public string Scheme { get; set; } = "https";

    /// <summary>The registrable domain tenant careers pages live under.</summary>
    public string CareersDomain { get; set; } = "";

    /// <summary>A tenant's careers host; contains <c>{tenant}</c>, e.g. <c>{tenant}.{CareersDomain}</c>.</summary>
    public string CareersHostPattern { get; set; } = "";

    /// <summary>A tenant's careers board, such as <c>https://acme.careers.example.com</c>.</summary>
    public string BoardUrl(string organizationSlug) => BoardUrl(Scheme, CareersHostPattern, CareersDomain, organizationSlug);

    /// <summary>A tenant's careers board from the three settings; the API's <c>DomainOptions</c> builds its links with it too.</summary>
    public static string BoardUrl(string scheme, string hostPattern, string careersDomain, string organizationSlug)
    {
        ArgumentNullException.ThrowIfNull(hostPattern);
        return $"{scheme}://{hostPattern.Replace("{CareersDomain}", careersDomain, StringComparison.Ordinal).Replace("{tenant}", organizationSlug, StringComparison.Ordinal)}";
    }
}

/// <summary>What a valid questionnaire link names: an organization and one of its questionnaire requests.</summary>
public sealed record QuestionnaireLink(Guid OrganizationId, Guid RequestId);

/// <summary>
/// The signed links to follow-up questionnaires (workflow-engine.md §8). A link's token is the organization and the
/// request, protected with ASP.NET Core Data Protection under its own purpose, as the apply form's telemetry sessions
/// are (<c>TelemetrySessions</c>), and time-limited. The worker issues tokens when a SendQuestionnaire action sends the
/// email; the API reads them when the careers page asks. Both share one key ring (<c>AddScreenHireDataProtection</c>).
/// </summary>
/// <remarks>
/// Whether a link has expired is the request's <see cref="QuestionnaireRequest.ExpiresAt"/>, checked against the clock
/// the caller passes, so an expired link can say so rather than look forged, and sending again revives every link to the
/// request. The token's own limit is that expiry plus <see cref="Grace"/>, after which it is refused like a forged one.
/// </remarks>
public sealed class QuestionnaireLinks(IDataProtectionProvider dataProtection, IOptions<CareersSiteOptions> careers)
{
    /// <summary>How long after its request's expiry a token still reads, so the page can say the link expired.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private readonly ITimeLimitedDataProtector protector =
        dataProtection.CreateProtector("ScreenHire.Questionnaire.Link.v1").ToTimeLimitedDataProtector();

    /// <summary>The token for <paramref name="request"/>, valid until its expiry plus <see cref="Grace"/>.</summary>
    public string Issue(QuestionnaireRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return protector.Protect(
            string.Create(CultureInfo.InvariantCulture, $"{request.OrganizationId:N}.{request.Id:N}"),
            request.ExpiresAt + Grace);
    }

    /// <summary>False without a careers host pattern (<c>ScreenHire:Domains</c>): a link would name no host.</summary>
    public bool IsConfigured => careers.Value.CareersHostPattern.Contains("{tenant}", StringComparison.Ordinal);

    /// <summary>The careers page for <paramref name="token"/> on the organization's careers board.</summary>
    public string Url(string organizationSlug, string token) => $"{careers.Value.BoardUrl(organizationSlug)}/questionnaires/{token}";

    /// <summary>What <paramref name="token"/> names, or null when it is forged, damaged or past its own limit.</summary>
    public QuestionnaireLink? Read(string? token)
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
            && Guid.TryParseExact(parts[1], "N", out var request)
                ? new QuestionnaireLink(organization, request)
                : null;
    }
}

public static class QuestionnaireLinkRegistration
{
    /// <summary>
    /// The link signer and the careers settings it builds URLs from. The API and the worker both need them. Data
    /// Protection is added too, so any host that runs the automation handlers can resolve the signer; the API and the
    /// worker also call <c>AddScreenHireDataProtection</c>, which keeps the shared key ring in Postgres.
    /// </summary>
    public static IServiceCollection AddQuestionnaireLinks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDataProtection();
        services.AddOptions<CareersSiteOptions>().BindConfiguration(CareersSiteOptions.Section);
        services.TryAddSingleton<QuestionnaireLinks>();
        return services;
    }
}
