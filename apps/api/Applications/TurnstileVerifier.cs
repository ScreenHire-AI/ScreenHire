using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Checks a Cloudflare Turnstile token from the careers form. The result is recorded on the application as a signal
/// (<c>AUTO_TURNSTILE_FAIL</c>) and never refuses a submission: a candidate with JavaScript off, or behind a strict
/// privacy tool, cannot pass a challenge, and verification signals never drive rejection (CLAUDE.md non-negotiable 2).
/// </summary>
internal sealed partial class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
{
    public bool IsConfigured => options.Value.IsConfigured;

    public string? SiteKey => IsConfigured ? options.Value.SiteKey : null;

    public async Task<BotCheckResult> VerifyAsync(string? token, string? remoteAddress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return BotCheckResult.NotChecked;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return BotCheckResult.Failed;
        }

        var form = new Dictionary<string, string> { ["secret"] = settings.SecretKey!, ["response"] = token };
        if (remoteAddress is not null)
        {
            form["remoteip"] = remoteAddress;
        }

        try
        {
            using var response = await http.PostAsync(new Uri(settings.VerifyUrl), new FormUrlEncodedContent(form), cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            return result?.Success == true ? BotCheckResult.Passed : BotCheckResult.Failed;
        }
        // Every failure but the caller's own cancellation: besides HTTP and JSON errors, the standard resilience handler
        // (ServiceDefaults) throws its own timeout and open-circuit exceptions, and none of them may lose an application.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Cloudflare being unreachable says nothing about the candidate, so it is recorded as no check at all.
            LogUnavailable(logger, exception);
            return BotCheckResult.NotChecked;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile verification was unavailable; the application is recorded as not checked")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
