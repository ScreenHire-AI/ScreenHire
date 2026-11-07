using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Configuration;

/// <summary>
/// OAuth sign-in for the MCP server, <c>ScreenHire:OAuth:*</c> (docs/architecture/oauth-server.md, ADR-0028). The API
/// reads these; the worker's purge keeps rows past their own expiry and does not read them.
/// </summary>
public sealed class OAuthOptions
{
    public const string Section = "ScreenHire:OAuth";

    /// <summary>How long an access token works. A client refreshes it, so a short life limits a leaked token.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a refresh token works unless it is used. Each use issues a new one with a fresh lifetime.</summary>
    [Range(typeof(TimeSpan), "01:00:00", "90.00:00:00")]
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long an authorization code works; a client exchanges it at once.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "00:10:00")]
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Client registrations per client address per hour. Counted per API instance.</summary>
    [Range(1, 10_000)]
    public int RegistrationsPerHour { get; set; } = 20;

    /// <summary>Requests per minute per client address to the token and revocation endpoints. Counted per API instance.</summary>
    [Range(1, 10_000)]
    public int TokenRequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Registrations nobody has approved yet that the deployment keeps. At the cap, registration answers 429 until the
    /// worker's daily purge removes the old ones, so open registration cannot fill the table.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxUnapprovedClients { get; set; } = 2_000;
}
