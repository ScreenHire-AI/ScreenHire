using System.ComponentModel.DataAnnotations;

namespace ScreenHire.Api.Configuration;

/// <summary>
/// The public apply flow (<c>ScreenHire:Apply</c>): candidate uploads and the rate limit on the careers write routes.
/// </summary>
public sealed class ApplyOptions
{
    public const string Section = "ScreenHire:Apply";

    /// <summary>The rate limiter policy on the public apply routes, partitioned by the candidate's address.</summary>
    public const string RateLimitPolicy = "apply";

    /// <summary>
    /// The rate limiter policy on telemetry ingest, apart from <see cref="RateLimitPolicy"/> so a page's beacons never use
    /// up the allowance its own submission needs.
    /// </summary>
    public const string TelemetryRateLimitPolicy = "telemetry";

    /// <summary>The blob container candidate uploads go to.</summary>
    [Required, RegularExpression("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$")]
    public string Container { get; set; } = "applicant-uploads";

    /// <summary>The largest file a candidate may upload.</summary>
    [Range(1, 100L * 1024 * 1024)]
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>How long an upload link stays valid. Long enough for a slow phone upload of the largest file.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan UploadLinkLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Requests to the apply write routes one candidate address may make per minute.</summary>
    [Range(1, 100_000)]
    public int PermitsPerMinute { get; set; } = 60;

    /// <summary>Telemetry snapshots one candidate address may send per minute. A page sends one each time it is hidden.</summary>
    [Range(1, 100_000)]
    public int TelemetryPermitsPerMinute { get; set; } = 120;
}

/// <summary>
/// Cloudflare Turnstile (<c>ScreenHire:Turnstile</c>), the careers form's optional bot challenge. Off unless both keys
/// are set, which is never the case locally or in tests: its outcome is an integrity signal, never a gate.
/// </summary>
public sealed class TurnstileOptions
{
    public const string Section = "ScreenHire:Turnstile";

    /// <summary>The public site key the careers form renders the widget with.</summary>
    public string? SiteKey { get; set; }

    /// <summary>The secret the API verifies tokens with. A user secret or key vault value, never a committed file.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Cloudflare's token verification endpoint.</summary>
    [Required, Url]
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
