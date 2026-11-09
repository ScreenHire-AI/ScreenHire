using System.Globalization;
using System.Text;

namespace ScreenHire.Application.Verification;

/// <summary>
/// RFC 4180 CSV for the AI usage export: CRLF rows, a byte order mark so a spreadsheet reads UTF-8, and any text cell that
/// starts like a formula neutralised with a leading apostrophe (as the dashboard's own CSV export does). Provider ids and
/// model names come from outside, so a cell is never trusted to be a plain value.
/// </summary>
public sealed class AiUsageCsv
{
    private readonly StringBuilder text = new();

    /// <summary>The characters that start a formula in a spreadsheet.</summary>
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>Adds a row. Strings are neutralised and quoted as needed; numbers are written as they are; null is an empty cell.</summary>
    public AiUsageCsv Row(params object?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append(cells[i] switch
            {
                null => "",
                string s => Quote(Neutralize(s)),
                DateTimeOffset when1 => when1.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                decimal d => d.ToString("0.########", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                var other => Quote(Neutralize(other.ToString() ?? "")),
            });
        }

        text.Append("\r\n");
        return this;
    }

    /// <summary>The file: a byte order mark, then the rows.</summary>
    public byte[] ToBytes() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];

    public static string Neutralize(string value) => value.Length > 0 && Array.IndexOf(FormulaStarts, value[0]) >= 0 ? "'" + value : value;

    private static string Quote(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
