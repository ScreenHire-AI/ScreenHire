using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// A member's calendar tokens at rest: ASP.NET Core Data Protection, whose key ring the API and the worker share in
/// Postgres (<c>AddScreenHireDataProtection</c>). Each organization and member has their own purpose, so a token copied
/// into another member's row does not read.
/// </summary>
public sealed class CalendarTokens(IDataProtectionProvider dataProtection)
{
    public const string Purpose = "ScreenHire.Calendar.Token.v1";

    public string Protect(Guid organizationId, Guid userId, string token) => Protector(organizationId, userId).Protect(token);

    /// <summary>The token, or null when it no longer reads (the key ring lost its key, or the row was tampered with).</summary>
    public string? Unprotect(Guid organizationId, Guid userId, string protectedToken)
    {
        try
        {
            return Protector(organizationId, userId).Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector Protector(Guid organizationId, Guid userId) =>
        dataProtection.CreateProtector(Purpose, organizationId.ToString("N"), userId.ToString("N"));
}

/// <summary>What an OAuth <c>state</c> carries: who started the round trip, with which provider, where, and the nonce its cookie holds.</summary>
public sealed record CalendarOAuthStateData(CalendarProvider Provider, Guid OrganizationId, Guid UserId, string Nonce, string ReturnUrl);

/// <summary>
/// The OAuth <c>state</c> of a round trip to a calendar provider: the round trip's facts, protected and time-limited
/// with Data Protection under its own purpose, as Slack's are. The callback also checks a cookie holding the nonce, so a
/// state copied out of one browser does not work in another.
/// </summary>
public sealed class CalendarOAuthState(IDataProtectionProvider dataProtection, TimeProvider time)
{
    public const string Purpose = "ScreenHire.Calendar.OAuthState.v1";

    /// <summary>How long someone has to finish on the provider's page.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>A new state and the nonce its cookie must carry.</summary>
    public (string State, string Nonce) Issue(CalendarProvider provider, Guid organizationId, Guid userId, string returnUrl)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var data = new CalendarOAuthStateData(provider, organizationId, userId, nonce, returnUrl);
        return (Protector.Protect(JsonSerializer.Serialize(data), time.GetUtcNow() + Lifetime), nonce);
    }

    /// <summary>What <paramref name="state"/> says, or null when it is forged, damaged or expired.</summary>
    public CalendarOAuthStateData? Read(string? state)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 4000)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CalendarOAuthStateData>(Protector.Unprotect(state, out var expiration)) is { } data
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
