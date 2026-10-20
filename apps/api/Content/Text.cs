namespace ScreenHire.Api.Content;

/// <summary>Plain-text input clean-up shared by the endpoints.</summary>
internal static class Text
{
    /// <summary>The trimmed text, or null when it is missing or only whitespace.</summary>
    public static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
