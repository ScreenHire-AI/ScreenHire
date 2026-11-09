using System.Text.RegularExpressions;
using ScreenHire.Domain.History;

namespace ScreenHire.Domain.Jobs;

/// <summary>
/// A job's short summary: the line a careers card shows under the title. The recruiter may write one (at most
/// <see cref="MaxLength"/> characters, about the job rather than the company). Until they do, the card uses the first
/// sentence of the description, cut at a word, so every job has one without anyone writing it.
/// </summary>
public static partial class JobSummaryText
{
    public const int MaxLength = 160;

    private const string Ellipsis = "…";

    /// <summary>A written summary as it is stored: whitespace collapsed to single spaces, and null when nothing is left.</summary>
    public static string? Normalize(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return Whitespace().Replace(summary, " ").Trim();
    }

    /// <summary>
    /// The first sentence of the first paragraph of a description (sanitized HTML), at most <see cref="MaxLength"/>
    /// characters. A heading or a list before the first paragraph is skipped. A sentence that is too long is cut at the
    /// last whole word that fits, with an ellipsis. Empty when the description has no paragraph text.
    /// </summary>
    public static string FromDescription(string? descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return "";
        }

        var plain = RichTextToPlain.Convert(Heading().Replace(descriptionHtml, ""));
        var paragraph = (plain ?? "")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(block => !ListItem().IsMatch(block));
        if (paragraph is null)
        {
            return "";
        }

        var text = Whitespace().Replace(paragraph, " ").Trim();
        var sentence = Sentence().Match(text);
        return CutAtWord(sentence.Success ? sentence.Value : text);
    }

    private static string CutAtWord(string text)
    {
        if (text.Length <= MaxLength)
        {
            return text;
        }

        // Room for the ellipsis, then back to the last space so no word is cut in half.
        var room = text[..(MaxLength - Ellipsis.Length)];
        var space = room.LastIndexOf(' ');
        var cut = space > 0 ? room[..space] : room;
        return cut.TrimEnd(' ', ',', ';', ':', '.', '-') + Ellipsis;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // A heading is a label ("About the role"), not a sentence about the job.
    [GeneratedRegex(@"<h[1-6][^>]*>.*?</h[1-6]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?:- |\d+\. )")]
    private static partial Regex ListItem();

    // Up to the first full stop, question mark or exclamation mark that ends a word.
    [GeneratedRegex(@"^.+?[.!?](?=\s|$)")]
    private static partial Regex Sentence();
}
