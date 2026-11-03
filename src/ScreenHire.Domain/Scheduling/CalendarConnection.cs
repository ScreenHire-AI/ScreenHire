using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Scheduling;

/// <summary>Whose calendar a member connected. <see cref="Sandbox"/> is the local stand-in (Development and tests only).</summary>
public enum CalendarProvider
{
    Google,
    Microsoft,
    Sandbox,
}

/// <summary>Why a connection stopped working until the member connects again.</summary>
public enum CalendarProblem
{
    /// <summary>The provider no longer accepts the member's tokens (revoked, expired for good, or unreadable here).</summary>
    NeedsReconnect,
}

/// <summary>
/// One member's calendar, connected by them through the provider's OAuth consent, so ScreenHire can read when they are
/// busy and put interviews in their calendar. One per member. The row holds the provider's tokens, protected at rest, so
/// change history never reads it (ADR-0016): the sheet shows the provider and the account address instead.
/// </summary>
public sealed class CalendarConnection : ITenantOwned
{
    public const int MaxTokenLength = 4000;

    public const int MaxAccountEmailLength = 320;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid UserId { get; init; }

    public CalendarProvider Provider { get; private set; }

    /// <summary>The calendar owner's address when the provider says it, to show which account it is. Optional.</summary>
    public string? AccountEmail { get; private set; }

    /// <summary>The access token, protected with Data Protection under the organization's and member's own purpose.</summary>
    public string ProtectedAccessToken { get; private set; } = "";

    /// <summary>The refresh token, protected the same way. Empty when the provider gave none.</summary>
    public string ProtectedRefreshToken { get; private set; } = "";

    public DateTimeOffset AccessTokenExpiresAt { get; private set; }

    /// <summary>Set when the tokens stopped working; cleared by connecting again.</summary>
    public CalendarProblem? Problem { get; private set; }

    public DateTimeOffset? ProblemAt { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public static CalendarConnection Connect(
        Guid organizationId,
        Guid userId,
        CalendarProvider provider,
        string? accountEmail,
        string protectedAccessToken,
        string protectedRefreshToken,
        DateTimeOffset accessTokenExpiresAt,
        DateTimeOffset now) =>
        new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Provider = provider,
            AccountEmail = Trim(accountEmail),
            ProtectedAccessToken = protectedAccessToken,
            ProtectedRefreshToken = protectedRefreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            ConnectedAt = now,
        };

    /// <summary>Connects again, perhaps another provider or account: whatever failed before no longer applies.</summary>
    public void Reconnect(
        CalendarProvider provider,
        string? accountEmail,
        string protectedAccessToken,
        string protectedRefreshToken,
        DateTimeOffset accessTokenExpiresAt,
        DateTimeOffset now)
    {
        Provider = provider;
        AccountEmail = Trim(accountEmail);
        ProtectedAccessToken = protectedAccessToken;
        ProtectedRefreshToken = protectedRefreshToken;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        ConnectedAt = now;
        Problem = null;
        ProblemAt = null;
    }

    /// <summary>
    /// Stores the tokens a refresh returned. The refresh token changes only when the provider rotated it (Microsoft
    /// does); <paramref name="protectedRefreshToken"/> null keeps the old one.
    /// </summary>
    public void Refreshed(string protectedAccessToken, string? protectedRefreshToken, DateTimeOffset accessTokenExpiresAt)
    {
        ProtectedAccessToken = protectedAccessToken;
        if (protectedRefreshToken is not null)
        {
            ProtectedRefreshToken = protectedRefreshToken;
        }

        AccessTokenExpiresAt = accessTokenExpiresAt;
    }

    public void RecordProblem(DateTimeOffset now)
    {
        if (Problem is null)
        {
            Problem = CalendarProblem.NeedsReconnect;
            ProblemAt = now;
        }
    }

    private static string? Trim(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        return trimmed.Length > MaxAccountEmailLength ? trimmed[..MaxAccountEmailLength] : trimmed;
    }
}
