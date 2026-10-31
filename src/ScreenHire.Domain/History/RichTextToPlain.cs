using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreenHire.Domain.History;

/// <summary>
/// Rich text (a job description, an email template's message) as text for the History sheet's before and after: rich
/// text is stored as sanitized HTML, and markup never reaches the screen. Blocks are separated by a blank line, a line
/// break is a newline, list items start with "- " or their number, a link shows its address after its text when the two
/// differ, every other tag goes, and entities are decoded. The input has already been sanitized on the way in; the
/// output is shown as text, never as HTML.
/// </summary>
public static partial class RichTextToPlain
{
    public static string? Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = new StringBuilder();
        var lists = new Stack<(bool Numbered, int Count)>();
        var links = new Stack<(string? Href, int Start)>();
        var position = 0;
        foreach (Match tag in Tag().Matches(html))
        {
            text.Append(html, position, tag.Index - position);
            position = tag.Index + tag.Length;
            var closing = tag.Groups[1].Value == "/";
            var name = tag.Groups[2].Value.ToLowerInvariant();
            switch (name)
            {
                case "br":
                    text.Append('\n');
                    break;
                case "ul" or "ol" when closing:
                    lists.TryPop(out _);
                    text.Append("\n\n");
                    break;
                case "ul" or "ol":
                    lists.Push((name == "ol", 0));
                    text.Append("\n\n");
                    break;
                case "li" when !closing:
                    var (numbered, count) = lists.TryPop(out var list) ? list : (false, 0);
                    lists.Push((numbered, count + 1));
                    text.Append('\n').Append(' ', 2 * Math.Max(lists.Count - 1, 0)).Append(numbered ? $"{count + 1}. " : "- ");
                    break;
                case "p" or "div" or "blockquote" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    text.Append("\n\n");
                    break;
                case "a" when !closing:
                    var href = Href().Match(tag.Groups[3].Value);
                    links.Push((href.Success ? WebUtility.HtmlDecode(href.Groups[1].Value) : null, text.Length));
                    break;
                case "a":
                    if (links.TryPop(out var link) && link.Href is { } address)
                    {
                        var shown = WebUtility.HtmlDecode(text.ToString(link.Start, text.Length - link.Start)).Trim();
                        if (address != shown && address != "mailto:" + shown)
                        {
                            text.Append(" (").Append(WebUtility.HtmlEncode(address)).Append(')');
                        }
                    }

                    break;
            }
        }

        text.Append(html, position, html.Length - position);
        var plain = WebUtility.HtmlDecode(text.ToString());
        plain = SpaceAroundBreak().Replace(plain, "\n");
        plain = BlankLines().Replace(plain, "\n\n");
        return plain.Trim();
    }

    [GeneratedRegex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"href\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    // Spaces at the end of a line, or at the start of one that is not a nested list item.
    [GeneratedRegex(@"[ \t]+\n|\n[ \t]+(?![ \t]*(?:- |\d+\. ))")]
    private static partial Regex SpaceAroundBreak();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
