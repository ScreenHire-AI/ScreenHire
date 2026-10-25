using System.Net;
using System.Text.RegularExpressions;

namespace ScreenHire.Domain.History;

/// <summary>
/// A job description as text for the History sheet's before and after: rich text is stored as sanitized HTML, and
/// markup never reaches the screen. Block elements become line breaks, every other tag goes, and entities are decoded.
/// The input has already been sanitized on the way in; the output is shown as text, never as HTML.
/// </summary>
public static partial class RichTextToPlain
{
    public static string? Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = LineBreak().Replace(html, "\n");
        text = ListStart().Replace(text, "\n");
        text = ListItem().Replace(text, "- ");
        text = BlockEnd().Replace(text, "\n");
        text = Tag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = SpaceBeforeBreak().Replace(text, "\n");
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<(ul|ol)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListStart();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"</(p|div|li|h[1-6]|ul|ol|blockquote|pre)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex SpaceBeforeBreak();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
