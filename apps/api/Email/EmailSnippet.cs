using System.Text.RegularExpressions;

namespace ScreenHire.Api.Email;

/// <summary>
/// The first line of an email's text part, for its row in Messages: the first line with words in it, with the quoted
/// email under a reply left out, its spaces collapsed and cut at a word to <see cref="MaxLength"/> characters.
/// </summary>
internal static partial class EmailSnippet
{
    /// <summary>The most characters a snippet keeps, ellipsis included.</summary>
    public const int MaxLength = 140;

    /// <summary>How much of a text part the list reads from the database: enough for a first line.</summary>
    public const int ReadLength = 400;

    /// <summary>The first line with words, or null when there is none before the quoted email.</summary>
    public static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = Spaces().Replace(raw, " ").Trim();
            if (line.Length == 0 || line.StartsWith('>'))
            {
                continue;
            }

            // A mail program's "On Tue, Nov 17, Avery wrote:" starts the quoted email: nothing after it is the reply's own.
            if (QuoteHeader().IsMatch(line))
            {
                return null;
            }

            return Cut(line);
        }

        return null;
    }

    private static string Cut(string line)
    {
        if (line.Length <= MaxLength)
        {
            return line;
        }

        var cut = line[..(MaxLength - 1)];
        var space = cut.LastIndexOf(' ');
        return $"{(space > MaxLength / 2 ? cut[..space] : cut).TrimEnd(' ', ',', ';', ':', '.')}…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^On .+ wrote:$")]
    private static partial Regex QuoteHeader();
}
