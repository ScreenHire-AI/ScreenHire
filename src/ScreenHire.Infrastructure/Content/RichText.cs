using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace ScreenHire.Infrastructure.Content;

/// <summary>
/// Rich text (job descriptions, template description defaults, email bodies) as one stored format: sanitized HTML
/// limited to what the dashboard's editor toolbar makes. <see cref="Sanitize"/> runs on the way in, so every stored
/// value is safe to render as HTML on careers pages, in feeds and in email. No scripts, styles, images, iframes,
/// classes or inline CSS. Links keep absolute <c>http</c>, <c>https</c> and <c>mailto</c> targets and open with
/// <c>rel="nofollow noopener noreferrer"</c>. docs/architecture/rich-text.md describes the canonical form.
/// </summary>
/// <remarks>
/// <see cref="HtmlSanitizer.Sanitize"/> is safe to call from many threads once configured, and nothing changes the
/// configuration after construction, so one instance serves every request.
/// </remarks>
public static partial class RichText
{
    /// <summary>The block elements the canonical form has at its top level.</summary>
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase) { "p", "h2", "h3", "ul", "ol" };

    /// <summary>Tags with the same meaning as one of the toolbar's, renamed rather than lost.</summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "strong", ["i"] = "em", ["h1"] = "h2", ["h4"] = "h3", ["h5"] = "h3", ["h6"] = "h3",
    };

    /// <summary>Deeper than anything the editor or a pasted document makes, far short of what overflows the stack.</summary>
    private const int MaxDepth = 100;

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    private static readonly HtmlSanitizer Sanitizer = Create();

    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// The canonical sanitized HTML of <paramref name="input"/>, which may be HTML or plain text. Only the toolbar's
    /// elements stay (<c>p br strong em h2 h3 ul ol li a</c>); synonyms are renamed (<c>b</c> to <c>strong</c>, <c>h4</c>
    /// to <c>h3</c>), and any other tag goes with its text kept. Loose text becomes paragraphs: a blank line starts a
    /// new one, a single newline is a line break, and lines that all start with "- " make a list. Whitespace inside a
    /// block collapses and empty blocks go. Sanitizing the result again gives the same string.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        var text = input.ReplaceLineEndings("\n");
        if (Deepest(Fragment(text)) > MaxDepth)
        {
            // HtmlSanitizer and AngleSharp walk the tree recursively, and markup nested thousands deep (a request may
            // carry 50,000 characters) overflows the stack, which no catch can stop. Nothing the editor or a pasted
            // document makes comes close, so such input keeps its words and loses its markup.
            text = WebUtility.HtmlEncode(string.Join(' ', Texts(Fragment(text))));
        }

        var body = Fragment(Sanitizer.Sanitize(text));
        Rename(body);
        body = Fragment(Structure(body));
        Clean(body);
        return body.InnerHtml.Trim();
    }

    /// <summary>Whether <paramref name="html"/> has no text at all, only markup and whitespace.</summary>
    public static bool IsBlank(string? html) =>
        string.IsNullOrWhiteSpace(html) || Texts(Fragment(html)).Count == 0;

    /// <summary>
    /// Sanitized HTML as plain text, for an email's text part: a blank line between blocks, a line break for each
    /// <c>br</c>, "- " before each bulleted item and its number before each numbered one, and a link's address after
    /// its text when the two differ.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var blocks = new List<string>();
        var inline = new StringBuilder();
        foreach (var node in Fragment(html).ChildNodes)
        {
            if (node is IElement element && (Blocks.Contains(element.LocalName) || element.LocalName == "li"))
            {
                FlushInline(inline, blocks);
                blocks.Add(BlockText(element, ""));
            }
            else
            {
                AppendInline(node, inline);
            }
        }

        FlushInline(inline, blocks);
        return string.Join("\n\n", blocks.Where(block => block.Length > 0));
    }

    private static HtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            // The toolbar's elements, and the synonyms Rename turns into them.
            AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "p", "br", "strong", "em", "h2", "h3", "ul", "ol", "li", "a", "b", "i", "h1", "h4", "h5", "h6",
            },
            AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href" },
            AllowedSchemes = AllowedSchemes,
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

        // A link goes somewhere absolute: a relative address means nothing on a careers page, a board or in an email.
        sanitizer.FilterUrl += (_, e) =>
        {
            if (!Uri.TryCreate(e.OriginalUrl, UriKind.Absolute, out var uri) || !AllowedSchemes.Contains(uri.Scheme))
            {
                e.SanitizedUrl = null;
            }
        };

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IHtmlAnchorElement anchor && anchor.HasAttribute("href"))
            {
                anchor.SetAttribute("rel", "nofollow noopener noreferrer");
            }
        };

        return sanitizer;
    }

    private static IElement Fragment(string html)
    {
        var document = Parser.ParseDocument("");
        var body = document.Body!;
        body.InnerHtml = html;
        return body;
    }

    /// <summary>
    /// How deep <paramref name="root"/>'s elements nest, counted without recursion. The canonical form is at most six
    /// deep (a link in italic in bold in a paragraph in a list item in a list).
    /// </summary>
    private static int Deepest(INode root)
    {
        var deepest = 0;
        var pending = new Stack<(INode Node, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var item))
        {
            deepest = Math.Max(deepest, item.Depth);
            foreach (var child in item.Node.ChildNodes)
            {
                pending.Push((child, item.Depth + 1));
            }
        }

        return deepest;
    }

    /// <summary>The text of <paramref name="root"/> in document order, without recursion.</summary>
    private static List<string> Texts(INode root)
    {
        var texts = new List<string>();
        var pending = new Stack<INode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node is IText text && !string.IsNullOrWhiteSpace(text.Data))
            {
                texts.Add(text.Data.Trim());
            }

            for (var i = node.ChildNodes.Length - 1; i >= 0; i--)
            {
                pending.Push(node.ChildNodes[i]);
            }
        }

        return texts;
    }

    private static void Rename(IElement root)
    {
        foreach (var element in root.QuerySelectorAll("*").ToList())
        {
            if (Synonyms.TryGetValue(element.LocalName, out var name))
            {
                var renamed = element.Owner!.CreateElement(name);
                foreach (var attribute in element.Attributes.ToList())
                {
                    renamed.SetAttribute(attribute.Name, attribute.Value);
                }

                while (element.FirstChild is { } child)
                {
                    renamed.AppendChild(child);
                }

                element.Replace(renamed);
            }
        }
    }

    /// <summary>
    /// The top level as blocks: block elements as they are, and each run of loose text and inline elements between them
    /// as paragraphs and lists, read the way the plain-text box read them.
    /// </summary>
    private static string Structure(IElement body)
    {
        var html = new StringBuilder();
        var run = new StringBuilder();
        var items = new List<string>();

        foreach (var node in body.ChildNodes)
        {
            if (node is IElement element && Blocks.Contains(element.LocalName))
            {
                FlushRun(run, html);
                html.Append(element.OuterHtml);
            }
            else if (node is IElement { LocalName: "li" } item)
            {
                // A list item with no list around it.
                FlushRun(run, html);
                items.Add(item.InnerHtml);
                if (item.NextElementSibling is not { LocalName: "li" })
                {
                    html.Append("<ul>").AppendJoin("", items.Select(inner => $"<li>{inner}</li>")).Append("</ul>");
                    items.Clear();
                }
            }
            else
            {
                run.Append(node is IElement inline ? inline.OuterHtml : node is IText text ? Encode(text.Data) : "");
            }
        }

        FlushRun(run, html);
        return html.ToString();
    }

    private static void FlushRun(StringBuilder run, StringBuilder html)
    {
        var items = new List<string>();
        void EndList()
        {
            if (items.Count > 0)
            {
                html.Append("<ul>").AppendJoin("", items.Select(item => $"<li>{item}</li>")).Append("</ul>");
                items.Clear();
            }
        }

        foreach (var block in BlankLines().Split(run.ToString()))
        {
            var lines = block.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            if (lines.All(line => line.StartsWith("- ", StringComparison.Ordinal)))
            {
                items.AddRange(lines.Select(line => line[2..].Trim()));
            }
            else
            {
                EndList();
                html.Append("<p>").AppendJoin("<br>", lines).Append("</p>");
            }
        }

        EndList();
        run.Clear();
    }

    /// <summary>Whitespace, empty blocks and wrappers as the editor would leave them.</summary>
    private static void Clean(IElement body)
    {
        // A list item holds its text directly: <li><p>One</p></li> is <li>One</li>, and two paragraphs are two lines.
        foreach (var item in body.QuerySelectorAll("li").ToList())
        {
            var paragraphs = item.Children.Where(child => child.LocalName == "p").ToList();
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                if (i > 0)
                {
                    paragraph.Before(item.Owner!.CreateElement("br"));
                }

                while (paragraph.FirstChild is { } child)
                {
                    paragraph.Before(child);
                }

                paragraph.Remove();
            }
        }

        // A link whose target was refused is only its text.
        foreach (var anchor in body.QuerySelectorAll("a:not([href])").ToList())
        {
            Unwrap(anchor);
        }

        CollapseWhitespace(body);

        // Empty blocks go, innermost first, so a list whose items were all empty goes too.
        foreach (var element in body.QuerySelectorAll("p, h2, h3, li, ul, ol, strong, em, a").Reverse().ToList())
        {
            TrimBreaks(element);
            if (element.TextContent.Replace('\u00a0', ' ').Trim().Length == 0)
            {
                element.Remove();
            }
        }
    }

    private static void CollapseWhitespace(IElement body)
    {
        // Whitespace between blocks and between list items is formatting, not text.
        foreach (var container in new[] { body }.Concat(body.QuerySelectorAll("ul, ol")))
        {
            foreach (var text in container.ChildNodes.OfType<IText>().ToList())
            {
                if (string.IsNullOrWhiteSpace(text.Data))
                {
                    text.Remove();
                }
            }
        }

        // Inside a block, a run of spaces and newlines is one space, and a block or line starts and ends with text.
        foreach (var text in body.Descendants<IText>().ToList())
        {
            text.Data = Spaces().Replace(text.Data, " ");
        }

        foreach (var block in body.QuerySelectorAll("p, h2, h3, li"))
        {
            var texts = block.Descendants<IText>().ToList();
            for (var i = 0; i < texts.Count; i++)
            {
                if (i == 0 || StartsLine(texts[i]))
                {
                    texts[i].Data = texts[i].Data.TrimStart(' ');
                }

                if (i == texts.Count - 1 || EndsLine(texts[i]))
                {
                    texts[i].Data = texts[i].Data.TrimEnd(' ');
                }
            }
        }
    }

    // Whether only inline wrappers stand between the text and a line break or a block edge before it.
    private static bool StartsLine(INode node)
    {
        for (var current = node; current.Parent is IElement parent; current = parent)
        {
            if (current.PreviousSibling is { } previous)
            {
                return previous is IElement { LocalName: "br" or "ul" or "ol" };
            }

            if (Blocks.Contains(parent.LocalName) || parent.LocalName == "li")
            {
                return true;
            }
        }

        return true;
    }

    private static bool EndsLine(INode node)
    {
        for (var current = node; current.Parent is IElement parent; current = parent)
        {
            if (current.NextSibling is { } next)
            {
                return next is IElement { LocalName: "br" or "ul" or "ol" };
            }

            if (Blocks.Contains(parent.LocalName) || parent.LocalName == "li")
            {
                return true;
            }
        }

        return true;
    }

    // Line breaks at the start or end of a block draw nothing but an empty line.
    private static void TrimBreaks(IElement element)
    {
        while (element.FirstChild is { } first && (first is IElement { LocalName: "br" } || first is IText { Data.Length: 0 }))
        {
            first.RemoveFromParent();
        }

        while (element.LastChild is { } last && (last is IElement { LocalName: "br" } || last is IText { Data.Length: 0 }))
        {
            last.RemoveFromParent();
        }
    }

    private static void Unwrap(IElement element)
    {
        while (element.FirstChild is { } child)
        {
            element.Before(child);
        }

        element.Remove();
    }

    private static string Encode(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\u00a0", "&nbsp;");

    private static void FlushInline(StringBuilder inline, List<string> blocks)
    {
        var text = inline.ToString().Trim();
        if (text.Length > 0)
        {
            blocks.Add(text);
        }

        inline.Clear();
    }

    private static string BlockText(IElement element, string indent)
    {
        switch (element.LocalName)
        {
            case "ul":
            case "ol":
                var lines = new List<string>();
                var number = 0;
                foreach (var item in element.Children.Where(child => child.LocalName == "li"))
                {
                    number++;
                    var marker = element.LocalName == "ol" ? $"{number}. " : "- ";
                    lines.Add(indent + marker + ItemText(item, indent + new string(' ', marker.Length)));
                }

                return string.Join("\n", lines);
            case "li":
                return "- " + ItemText(element, "  ");
            default:
                var text = new StringBuilder();
                foreach (var child in element.ChildNodes)
                {
                    AppendInline(child, text);
                }

                return text.ToString().Trim();
        }
    }

    private static string ItemText(IElement item, string indent)
    {
        var text = new StringBuilder();
        foreach (var child in item.ChildNodes)
        {
            if (child is IElement { LocalName: "ul" or "ol" } list)
            {
                text.Append('\n').Append(BlockText(list, indent));
            }
            else if (child is IElement { LocalName: "p" or "h2" or "h3" } paragraph)
            {
                if (text.Length > 0)
                {
                    text.Append('\n').Append(indent);
                }

                text.Append(BlockText(paragraph, indent));
            }
            else
            {
                AppendInline(child, text);
            }
        }

        return text.ToString().Trim();
    }

    private static void AppendInline(INode node, StringBuilder text)
    {
        switch (node)
        {
            case IText leaf:
                var data = Spaces().Replace(leaf.Data.Replace('\u00a0', ' '), " ");
                text.Append(text.Length == 0 || text[^1] == '\n' ? data.TrimStart(' ') : data);
                break;
            case IElement { LocalName: "br" }:
                // A line break ends the line; the next one starts without the space the markup put after it.
                TrimEnd(text);
                text.Append('\n');
                break;
            case IElement { LocalName: "a" } anchor:
                var label = new StringBuilder();
                foreach (var child in anchor.ChildNodes)
                {
                    AppendInline(child, label);
                }

                var shown = label.ToString();
                var href = anchor.GetAttribute("href");
                text.Append(shown);
                if (!string.IsNullOrEmpty(href) && href != shown.Trim() && href != "mailto:" + shown.Trim())
                {
                    text.Append(" (").Append(href).Append(')');
                }

                break;
            case IElement element:
                foreach (var child in element.ChildNodes)
                {
                    AppendInline(child, text);
                }

                break;
        }
    }

    private static void TrimEnd(StringBuilder text)
    {
        while (text.Length > 0 && text[^1] == ' ')
        {
            text.Length--;
        }
    }

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"[ \t\r\n\f]+")]
    private static partial Regex Spaces();
}
