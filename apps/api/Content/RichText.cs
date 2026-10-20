using AngleSharp.Html.Dom;
using Ganss.Xss;

namespace ScreenHire.Api.Content;

/// <summary>
/// Sanitizes rich text (job descriptions, template description defaults) on the way in, so every stored description is
/// safe to render as HTML on careers pages, in feeds and in the dashboard. An allowlist of formatting tags only: no
/// scripts, styles, images, iframes, classes or inline CSS. Links keep <c>http</c>, <c>https</c> and <c>mailto</c>
/// targets and open with <c>rel="nofollow noopener noreferrer"</c>.
/// </summary>
/// <remarks>
/// <see cref="HtmlSanitizer.Sanitize"/> is safe to call from many threads once configured, and nothing changes the
/// configuration after construction, so one instance serves every request.
/// </remarks>
internal static class RichText
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    /// <summary>The sanitized HTML, trimmed. Disallowed tags are dropped with their content kept as text.</summary>
    public static string Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? "" : Sanitizer.Sanitize(html).Trim();

    private static HtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "a",
            },
            AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href" },
            AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" },
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href" },
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
        })
        {
            KeepChildNodes = true,
        };

        // KeepChildNodes keeps the text of unknown wrappers (div, span from a pasted document), but the content of these
        // elements is code or markup, not prose, so it goes with them.
        sanitizer.RemovingTag += (_, e) =>
        {
            if (e.Tag.LocalName is "script" or "style" or "template" or "noscript" or "iframe" or "object" or "svg" or "math" or "textarea" or "select")
            {
                e.Tag.TextContent = "";
            }
        };

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IHtmlAnchorElement anchor)
            {
                anchor.SetAttribute("rel", "nofollow noopener noreferrer");
            }
        };

        return sanitizer;
    }
}
