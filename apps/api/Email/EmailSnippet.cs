using System.Text.RegularExpressions;

namespace ScreenHire.Api.Email;

/// <summary>
/// The first line of an email's text part that says something, for its row in Messages: not a lone greeting ("Hi Casey,"),
/// and never the quoted email under a reply, with its spaces collapsed and cut at a word to <see cref="MaxLength"/>
/// characters. An email that is nothing but a greeting shows its greeting.
/// </summary>
internal static partial class EmailSnippet
{
    /// <summary>The most characters a snippet keeps, ellipsis included.</summary>
    public const int MaxLength = 140;

    /// <summary>How much of a text part the list reads from the database: enough for a first line.</summary>
    public const int ReadLength = 400;

    /// <summary>The first line that says something, or null when there is no line before the quoted email.</summary>
    public static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string? salutation = null;
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
                break;
            }

            if (!Greeting().IsMatch(line))
            {
                return Cut(line);
            }

            salutation ??= line;
        }

        return salutation is null ? null : Cut(salutation);
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

    // A line that is only a greeting ("Hi Taylor,", "Dear Ms Quinn", "Hello all"): it says who the email is to, not what it
    // says. After the salutation come at most three capitalised names or a word for a group, and nothing else, so a short
    // reply that opens with one ("Hi Avery, I can do Friday") is never skipped. The same rule as the Overview's Latest
    // message (`firstLine` in apps/dashboard/src/lib/components/candidate/latest-message-panel.svelte); change both together.
    [GeneratedRegex(@"^(?:[Hh]i|[Hh]ello|[Hh]ey|[Dd]ear|[Gg]ood (?:morning|afternoon|evening))(?:\s+(?:\p{Lu}[\p{L}.'-]*|there|all|team|everyone|folks)){0,3}\s*[,!]?$")]
    private static partial Regex Greeting();
}
