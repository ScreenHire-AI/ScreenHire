using System.Globalization;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.History;

/// <summary>
/// What the history may see of an API key: its name, scopes, rate limit and revocation. Never its hash, its prefix or
/// when it was last used, so none of them can reach a line (docs/architecture/change-history.md, "Secrets").
/// </summary>
public sealed record ApiKeyFacts(string Name, IReadOnlyList<ApiKeyScope> Scopes, int? PermitsPerMinute, DateTimeOffset? RevokedAt)
{
    public static ApiKeyFacts Of(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new(key.Name, [.. key.Scopes], key.PermitsPerMinute, key.RevokedAt);
    }
}

/// <summary>
/// The API keys page's changes in words: "created the key <b>Sync</b>" with its access, and "revoked the key
/// <b>Sync</b>". Nothing else about a key changes after it is made.
/// </summary>
public static class ApiKeyChanges
{
    /// <param name="before">The key before the save; null when the save added it.</param>
    /// <param name="after">The key after the save.</param>
    public static IReadOnlyList<ChangeLine> Describe(ApiKeyFacts? before, ApiKeyFacts? after)
    {
        if (after is null)
        {
            return [];
        }

        if (before is null)
        {
            List<ChangeDetail> details = [new("Access", null, ChangeLabels.ApiKeyAccess(after.Scopes))];
            if (after.PermitsPerMinute is { } permits)
            {
                details.Add(new("Requests per minute", null, permits.ToString("N0", CultureInfo.InvariantCulture)));
            }

            return [ChangeLine.Of("created the key ", after.Name, details: details)];
        }

        return before.RevokedAt is null && after.RevokedAt is not null ? [ChangeLine.Of("revoked the key ", after.Name)] : [];
    }
}
