namespace ScreenHire.Api.Content;

/// <summary>
/// A text search as a Postgres <c>LIKE</c> pattern: the trimmed text matched anywhere, with <c>%</c>, <c>_</c> and the
/// escape character taken literally, so a search for "50%" finds "50%" and not everything starting with "50".
/// </summary>
internal static class Like
{
    /// <summary>The escape character the patterns use, for <c>EF.Functions.ILike(value, pattern, Like.Escape)</c>.</summary>
    public const string Escape = "\\";

    /// <summary>The pattern for values containing <paramref name="text"/>, or null when there is nothing to search for.</summary>
    public static string? Contains(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace(Escape, Escape + Escape, StringComparison.Ordinal)
                .Replace("%", Escape + "%", StringComparison.Ordinal)
                .Replace("_", Escape + "_", StringComparison.Ordinal) + "%";
}
