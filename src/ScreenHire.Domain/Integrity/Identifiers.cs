namespace ScreenHire.Domain.Integrity;

/// <summary>
/// Normal forms of hard identifiers, shared by within-organization duplicate checks and the Signal Network's hashing
/// (integrity-engine.md §6), so "the same email" means one thing everywhere.
/// </summary>
public static class Identifiers
{
    /// <summary>
    /// The email trimmed, lower-cased, with any plus tag removed from the local part (<c>sam+jobs@example.com</c> is
    /// <c>sam@example.com</c>). Null for something that is not an address.
    /// </summary>
    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim().ToLowerInvariant();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
        {
            return null;
        }

        var local = trimmed[..at];
        var plus = local.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0)
        {
            local = local[..plus];
        }

        return $"{local}@{trimmed[(at + 1)..]}";
    }

    /// <summary>The domain part of an email address, lower-cased, or null.</summary>
    public static string? EmailDomain(string? email)
    {
        var normalized = NormalizeEmail(email);
        return normalized?[(normalized.LastIndexOf('@') + 1)..];
    }
}
