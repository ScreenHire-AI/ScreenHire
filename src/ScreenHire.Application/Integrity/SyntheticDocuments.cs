using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ScreenHire.Application.Integrity;

/// <summary>
/// Real, readable PDFs for the synthetic applicants and the tests (PdfPig's writer): visible text, optional white text a
/// person cannot see, and the producer and creation time a resume tool would write. The same inputs always give the
/// same bytes, so identical resumes share a file hash as real copies do.
/// </summary>
public static partial class SyntheticDocuments
{
    /// <summary>A one-page PDF. <paramref name="hiddenText"/> is drawn in white on the white page, near the bottom.</summary>
    /// <remarks><c>created</c>: the creation time the file claims, written in PDF date form.</remarks>
    public static byte[] Pdf(string text, string? producer = null, string? hiddenText = null, DateTimeOffset? created = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Producer = producer ?? "ScreenHire synthetic applicants";
        if (created is { } at)
        {
            builder.DocumentInformation.CreationDate = PdfDate(at);
        }

        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var y = 780.0;
        foreach (var line in Lines(text, 90))
        {
            page.AddText(line, 11, new PdfPoint(50, y), font);
            y -= 16;
        }

        if (hiddenText is not null)
        {
            page.SetTextAndFillColor(255, 255, 255);
            page.AddText(hiddenText, 8, new PdfPoint(40, 60), font);
            page.ResetColor();
        }

        return WithStableId(builder.Build());
    }

    /// <summary>
    /// The writer puts two random GUIDs in the trailer's <c>/ID</c>. They are replaced by a hash of the rest of the file,
    /// the same length, so every offset in the file stays where it was and equal content gives equal bytes.
    /// </summary>
    private static byte[] WithStableId(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var match = TrailerId().Match(text);
        if (!match.Success)
        {
            return pdf;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(text.Remove(match.Index, match.Length))))[..32];
        return Encoding.Latin1.GetBytes(text[..match.Index] + $"/ID [ <{hash}><{hash}>]" + text[(match.Index + match.Length)..]);
    }

    /// <summary>PDF's date form, <c>D:YYYYMMDDHHmmSSZ</c>, in UTC.</summary>
    public static string PdfDate(DateTimeOffset at) =>
        "D:" + at.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    [GeneratedRegex(@"/ID \[ <[0-9A-F]{32}><[0-9A-F]{32}>\]")]
    private static partial Regex TrailerId();

    // Splits into lines at line breaks, then wraps each at the width.
    private static IEnumerable<string> Lines(string text, int width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > width)
                {
                    yield return line;
                    line = "";
                }

                line = line.Length == 0 ? word : $"{line} {word}";
            }

            yield return line;
        }
    }
}
