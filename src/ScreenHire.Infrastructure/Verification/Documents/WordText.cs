using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Infrastructure.Verification.Documents;

/// <summary>
/// Word 2007+ text, metadata and hidden text through DocumentFormat.OpenXml. A run is hidden when it is marked hidden
/// (<c>w:vanish</c>), is under 2 points, or is white with no shading behind it: run, paragraph, table cell or table
/// shading all count, because resume templates put white text in shaded sidebar cells.
/// </summary>
internal static class WordText
{
    /// <summary>
    /// The most characters one part of the package may hold once unzipped, markup included. A long resume is well under
    /// 1 million; the cap also bounds the document tree the parser builds in memory.
    /// </summary>
    private const long MaxCharactersInPart = 5_000_000;

    /// <summary>Font sizes in Word are half-points; under 4 is under 2 points.</summary>
    private const int TinyHalfPoints = 4;

    public static ExtractedDocument Extract(byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        // A cap on how far one part may expand, so a crafted file (a zip bomb) cannot exhaust memory.
        using var document = WordprocessingDocument.Open(stream, isEditable: false, new OpenSettings { MaxCharactersInPart = MaxCharactersInPart });
        var body = document.MainDocumentPart?.Document?.Body;
        var lines = new List<string>();
        var hidden = new List<HiddenTextSpan>();
        if (body is not null)
        {
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = new StringBuilder();
                var shadedBehind = ShadedBehind(paragraph);
                HiddenTextSpan? open = null;
                foreach (var run in paragraph.Descendants<Run>())
                {
                    var text = string.Concat(run.Descendants<Text>().Select(t => t.Text));
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    if (Hidden(run.RunProperties, shadedBehind) is { } reason)
                    {
                        open = open is not null && open.Reason == reason
                            ? open with { Text = open.Text + text }
                            : Add(open, hidden) ?? new HiddenTextSpan(text, reason, 1);
                        continue;
                    }

                    open = Add(open, hidden);
                    line.Append(text);
                }

                Add(open, hidden);
                lines.Add(line.ToString());
            }
        }

        return new ExtractedDocument(
            DocumentExtractionStatus.Extracted,
            DocumentExtraction.Tidy(lines),
            1,
            Metadata(document),
            DocumentExtraction.Keep(hidden));
    }

    private static HiddenTextSpan? Add(HiddenTextSpan? open, List<HiddenTextSpan> hidden)
    {
        if (open is not null)
        {
            hidden.Add(open);
        }

        return null;
    }

    // Whether anything around the paragraph is shaded: the paragraph itself, or a table cell or table it sits in.
    private static bool ShadedBehind(Paragraph paragraph) =>
        IsShaded(paragraph.ParagraphProperties?.Shading)
        || paragraph.Ancestors<TableCell>().Any(cell => IsShaded(cell.TableCellProperties?.Shading))
        || paragraph.Ancestors<Table>().Any(table => IsShaded(table.GetFirstChild<TableProperties>()?.Shading));

    private static HiddenTextReason? Hidden(RunProperties? properties, bool shadedBehind)
    {
        if (properties is null)
        {
            return null;
        }

        if (properties.Vanish is { } vanish && (vanish.Val is null || vanish.Val.Value))
        {
            return HiddenTextReason.InvisibleText;
        }

        if (properties.FontSize?.Val?.Value is { } size && int.TryParse(size, out var halfPoints) && halfPoints < TinyHalfPoints)
        {
            return HiddenTextReason.TinyText;
        }

        var colour = properties.Color?.Val?.Value;
        var white = colour is not null && (colour.Equals("FFFFFF", StringComparison.OrdinalIgnoreCase) || colour.Equals("white", StringComparison.OrdinalIgnoreCase));
        return white && !shadedBehind && !IsShaded(properties.Shading) && properties.Highlight is null
            ? HiddenTextReason.WhiteText
            : null;
    }

    private static bool IsShaded(Shading? shading)
    {
        var fill = shading?.Fill?.Value;
        return fill is not null && !fill.Equals("auto", StringComparison.OrdinalIgnoreCase) && !fill.Equals("FFFFFF", StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentMetadata Metadata(WordprocessingDocument document)
    {
        var application = document.ExtendedFilePropertiesPart?.Properties?.Application?.Text;
        var properties = document.PackageProperties;
        return new DocumentMetadata(
            Producer: PdfText.Clean(application),
            CreatedAt: properties.Created is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created.ToUniversalTime(), DateTimeKind.Utc)) : null,
            ModifiedAt: properties.Modified is { } modified ? new DateTimeOffset(DateTime.SpecifyKind(modified.ToUniversalTime(), DateTimeKind.Utc)) : null);
    }
}
