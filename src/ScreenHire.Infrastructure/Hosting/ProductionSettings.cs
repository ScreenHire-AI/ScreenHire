using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ScreenHire.Infrastructure.Hosting;

/// <summary>Which process is starting, because the API and the worker need different settings.</summary>
public enum HostKind
{
    Api,
    Worker,
}

/// <summary>
/// The settings a host must have to run outside Development, and the local stand-ins that must be off there
/// (docs/operations/deploy.md, "Settings"). A host that is missing one stops at start with a message that names every
/// missing setting, instead of starting and failing on its first request. Optional providers are not here: a provider
/// with no configuration stays hidden (SendGrid, Slack, calendars, sign-in providers, Turnstile, Google Indexing,
/// the language model).
/// </summary>
public static class ProductionSettings
{
    /// <summary>The shortest device hash key accepted (<c>ScreenHire:Verification:DeviceHashKey</c>).</summary>
    public const int MinimumDeviceHashKeyLength = 32;

    private const string DataProtectionKeySetting = "ScreenHire:DataProtection:KeyVaultKeyUri";

    /// <summary>
    /// Stops the host with one message naming every problem, unless it is running in Development, where the AppHost
    /// supplies local values and the sandboxes are allowed.
    /// </summary>
    public static TBuilder RequireScreenHireProductionSettings<TBuilder>(this TBuilder builder, HostKind kind)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var problems = Problems(builder.Configuration, kind);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "ScreenHire cannot start outside Development with these settings missing or unsafe:"
                + string.Concat(problems.Select(problem => Environment.NewLine + " - " + problem)));
        }

        return builder;
    }

    /// <summary>Every missing required setting and every local stand-in that is on, for a host outside Development.</summary>
    public static IReadOnlyList<string> Problems(IConfiguration configuration, HostKind kind)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var problems = new List<string>();

        Require(configuration, problems, "ConnectionStrings:screenhire-app", "the PostgreSQL connection as screenhire_app");
        Require(configuration, problems, "ConnectionStrings:blobs", "the Blob Storage endpoint");
        Require(configuration, problems, "ScreenHire:Webhooks:SigningKey", "the key webhooks are signed with", inVault: true);
        // Explicitly allowing the key ring to stay unencrypted is for a test stack that runs as Production; a deployment sets the key.
        if (!(bool.TryParse(configuration["ScreenHire:DataProtection:AllowUnprotectedKeys"], out var allowUnprotected) && allowUnprotected))
        {
            Require(configuration, problems, DataProtectionKeySetting, "the Key Vault key that protects the Data Protection key ring");
        }

        if (kind == HostKind.Api)
        {
            if (!(configuration["ScreenHire:Verification:DeviceHashKey"] is { } key && key.Length >= MinimumDeviceHashKeyLength))
            {
                problems.Add($"ScreenHire:Verification:DeviceHashKey must be at least {MinimumDeviceHashKeyLength} characters "
                    + "(vault secret ScreenHire--Verification--DeviceHashKey): without it device fingerprints are not stored and "
                    + "device duplicates are not checked.");
            }

            if (!configuration.GetSection("ScreenHire:Auth:TrustedProxies").GetChildren().Any(proxy =>
                    proxy.Value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length > 0))
            {
                problems.Add("ScreenHire:Auth:TrustedProxies is empty (parameter auth-trusted-proxies): behind the platform's ingress "
                    + "every visitor would share one rate-limit bucket.");
            }

            Off(configuration, problems, "ScreenHire:CareersEmbedding:AllowLoopback");
        }
        else
        {
            // The worker builds links and sends mention emails from these (the API validates the same values).
            Require(configuration, problems, "ScreenHire:Domains:AppHost", "the dashboard host");
            Require(configuration, problems, "ScreenHire:Domains:CareersDomain", "the careers domain");
            Require(configuration, problems, "ScreenHire:Domains:CareersHostPattern", "the careers host pattern");
        }

        Off(configuration, problems, "ScreenHire:Webhooks:AllowLoopback");
        Off(configuration, problems, "ScreenHire:Distribution:Sandbox:Enabled");
        return problems;
    }

    private static void Require(IConfiguration configuration, List<string> problems, string key, string what, bool inVault = false)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            problems.Add($"{key} is not set ({what})."
                + (inVault ? $" It is the Key Vault secret {key.Replace(":", "--", StringComparison.Ordinal)}." : ""));
        }
    }

    private static void Off(IConfiguration configuration, List<string> problems, string key)
    {
        if (bool.TryParse(configuration[key], out var on) && on)
        {
            problems.Add($"{key} is true. It is a local stand-in and must be off outside Development.");
        }
    }
}
