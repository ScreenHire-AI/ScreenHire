using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Slack;

/// <summary>
/// Slack as this host is configured (<see cref="SlackOptions.Mode"/>): always registered, so every handler and endpoint
/// that takes it resolves, and <see cref="IsConfigured"/> false hides Slack, as <c>TransactionalEmail</c> does for email.
/// </summary>
/// <remarks>
/// Both clients are constructor parameters, not looked up from an <c>IServiceProvider</c>: Wolverine generates handler
/// code and refuses a handler whose dependency locates services at run time (<c>ServiceLocationPolicy.NotAllowed</c>).
/// </remarks>
public sealed class SlackAccess(IOptions<SlackOptions> options, SlackApiClient slackClient, SandboxSlackClient sandboxClient)
{
    public SlackMode Mode => options.Value.Mode;

    public bool IsConfigured => Mode != SlackMode.Off;

    /// <summary>The client for <see cref="Mode"/>. Ask <see cref="IsConfigured"/> first: with Slack off there is none.</summary>
    public ISlackClient Client => Mode switch
    {
        SlackMode.Slack => slackClient,
        SlackMode.Sandbox => sandboxClient,
        _ => throw new InvalidOperationException("Slack is not set up here (ScreenHire:Slack)."),
    };
}

/// <summary>
/// The bot token at rest: ASP.NET Core Data Protection, whose key ring the API and the worker share in Postgres
/// (<c>AddScreenHireDataProtection</c>). Each organization has its own purpose, so a token copied into another
/// organization's row does not read.
/// </summary>
public sealed class SlackTokens(IDataProtectionProvider dataProtection)
{
    public const string Purpose = "ScreenHire.Slack.BotToken.v1";

    public string Protect(Guid organizationId, string token) => Protector(organizationId).Protect(token);

    /// <summary>The token, or null when it no longer reads (the key ring lost its key, or the row was tampered with).</summary>
    public string? Unprotect(Guid organizationId, string protectedToken)
    {
        try
        {
            return Protector(organizationId).Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(Guid organizationId) => dataProtection.CreateProtector(Purpose, organizationId.ToString("N"));
}

/// <summary>Why an OAuth round trip to Slack started: adding the app, or linking a person's account.</summary>
public enum SlackOAuthPurpose
{
    Install,
    Link,
}

/// <summary>What an OAuth <c>state</c> carries: who started the round trip, where, and the nonce its cookie holds.</summary>
public sealed record SlackOAuthStateData(SlackOAuthPurpose Purpose, Guid OrganizationId, Guid UserId, string Nonce, string ReturnUrl);

/// <summary>
/// The OAuth <c>state</c> of a round trip to Slack: the round trip's facts, protected and time-limited with Data
/// Protection under its own purpose, as questionnaire links are. The callback also checks a cookie holding the
/// nonce, so a state copied out of one browser does not work in another.
/// </summary>
public sealed class SlackOAuthState(IDataProtectionProvider dataProtection, TimeProvider time)
{
    public const string Purpose = "ScreenHire.Slack.OAuthState.v1";

    /// <summary>How long someone has to finish on Slack's page.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>A new state and the nonce its cookie must carry.</summary>
    public (string State, string Nonce) Issue(SlackOAuthPurpose purpose, Guid organizationId, Guid userId, string returnUrl)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var data = new SlackOAuthStateData(purpose, organizationId, userId, nonce, returnUrl);
        return (Protector.Protect(JsonSerializer.Serialize(data), time.GetUtcNow() + Lifetime), nonce);
    }

    /// <summary>What <paramref name="state"/> says, or null when it is forged, damaged or expired.</summary>
    public SlackOAuthStateData? Read(string? state)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 4000)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SlackOAuthStateData>(Protector.Unprotect(state, out var expiration)) is { } data
                && expiration > time.GetUtcNow()
                ? data
                : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}

public static class SlackRegistration
{
    /// <summary>Slack's services, the same in the API and the worker. The mode is read per call, from <see cref="SlackOptions"/>.</summary>
    public static IServiceCollection AddScreenHireSlack(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<SlackOptions>().BindConfiguration(SlackOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SlackOptions>, SlackSandboxGate>());
        services.TryAddSingleton<SlackApiClient>();
        services.TryAddScoped<SandboxSlackClient>();
        services.TryAddScoped<SlackAccess>();
        services.TryAddSingleton<SlackTokens>();
        services.TryAddSingleton<SlackOAuthState>();
        return services;
    }
}
