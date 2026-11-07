using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Configuration;

/// <summary>
/// Every public host, as configuration (ADR-0003): <c>ScreenHire:Domains:*</c>. Nothing in the code names a
/// <c>screenhire.*</c> host. Development defaults are in <c>appsettings.Development.json</c>; the AppHost overrides
/// them with the real local endpoints, which use random ports in tests.
/// </summary>
public sealed class DomainOptions
{
    public const string Section = "ScreenHire:Domains";

    private const string HostPattern = @"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?(:\d{1,5})?$";

    /// <summary><c>https</c> everywhere except local development.</summary>
    [Required, RegularExpression("^https?$")]
    public string Scheme { get; set; } = "https";

    /// <summary>The marketing site's host, e.g. <c>example.com</c>.</summary>
    [Required, RegularExpression(HostPattern)]
    public string BaseDomain { get; set; } = "";

    /// <summary>
    /// The dashboard's host, with a port when there is one. The dashboard reaches the API through its own <c>/v1</c>
    /// path, so sign-in cookies, OIDC redirect URIs and emailed links all use this host.
    /// </summary>
    [Required, RegularExpression(HostPattern)]
    public string AppHost { get; set; } = "";

    /// <summary>The API's own host, for server-to-server callers such as the careers pages.</summary>
    [Required, RegularExpression(HostPattern)]
    public string ApiHost { get; set; } = "";

    /// <summary>The registrable domain tenant careers pages live under.</summary>
    [Required, RegularExpression(HostPattern)]
    public string CareersDomain { get; set; } = "";

    /// <summary>A tenant's careers host; must contain <c>{tenant}</c>, e.g. <c>{tenant}.{CareersDomain}</c>.</summary>
    [Required, RegularExpression(@"^.*\{tenant\}.*$")]
    public string CareersHostPattern { get; set; } = "";

    /// <summary>
    /// The sign-in cookie's <c>Domain</c> attribute. Leave it empty (the default) for a host-only cookie on
    /// <see cref="AppHost"/>: a parent-domain cookie would also reach tenant careers pages under the same apex.
    /// </summary>
    [RegularExpression(@"^\.?[a-z0-9]([a-z0-9.-]*[a-z0-9])?$")]
    public string? CookieDomain { get; set; }

    // Methods, not properties: options validation reads every property, including on an unconfigured instance.

    /// <summary>The dashboard origin, e.g. <c>https://app.example.com</c>.</summary>
    public Uri GetAppOrigin() => new($"{Scheme}://{AppHost}");

    /// <summary>An absolute URL on the dashboard origin for a local path such as <c>/sign-in</c>.</summary>
    public Uri AppUrl(string localPath) => new(GetAppOrigin(), localPath);

    /// <summary>The API's origin, e.g. <c>https://api.example.com</c>: the issuer of the OAuth metadata and the MCP server's host.</summary>
    public Uri GetApiOrigin() => new($"{Scheme}://{ApiHost}");

    /// <summary>An absolute URL on the API's origin for a local path such as <c>/v1/mcp</c>.</summary>
    public Uri ApiUrl(string localPath) => new(GetApiOrigin(), localPath);

    /// <summary>
    /// A tenant's careers board, such as <c>https://acme.careers.example.com</c>, from <see cref="CareersHostPattern"/>.
    /// Feeds, the public job-board API and Google's Indexing API all link here.
    /// </summary>
    public string CareersBoardUrl(string organizationSlug) =>
        Application.Applications.CareersSiteOptions.BoardUrl(Scheme, CareersHostPattern, CareersDomain, organizationSlug);

    /// <summary>
    /// The page on a tenant's careers board where a candidate asks for a link to their applications. The emails the team
    /// previews end with it in place of the candidate's own link, which only the worker issues.
    /// </summary>
    public string PortalEntryUrl(string organizationSlug) => $"{CareersBoardUrl(organizationSlug)}/my-applications";

    /// <summary>A job's page on its tenant's careers board: the URL boards link to and Google indexes.</summary>
    public string CareersJobUrl(string organizationSlug, string jobSlug) => $"{CareersBoardUrl(organizationSlug)}/jobs/{jobSlug}";
}
