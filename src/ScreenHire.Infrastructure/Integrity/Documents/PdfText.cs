using System.Xml.Linq;
using ScreenHire.Domain.Integrity;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace ScreenHire.Infrastructure.Integrity.Documents;

/// <summary>
/// PDF text, metadata and hidden text through PdfPig. A letter is hidden when it is drawn invisibly, sits off the page,
/// is under <see cref="TinyPointSize"/>, or is white with nothing darker drawn behind it. The last check matters: many
/// resume templates put white text on a dark sidebar, and that text is visible.
/// </summary>
internal static class PdfText
{
    /// <summary>Text smaller than this, in points, is not readable on paper or screen.</summary>
    public const double TinyPointSize = 2;

    /// <summary>Each colour channel at or above this (0 to 1) counts as white.</summary>
    private const double WhiteThreshold = 0.94;

    public static ExtractedDocument Extract(byte[] content, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(content, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        var lines = new List<string>();
        var hidden = new List<HiddenTextSpan>();
        var pages = Math.Min(document.NumberOfPages, DocumentExtraction.MaxPages);
        for (var number = 1; number <= pages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadPage(document.GetPage(number), lines, hidden);
        }

        return new ExtractedDocument(
            DocumentExtractionStatus.Extracted,
            DocumentExtraction.Tidy(lines),
            document.NumberOfPages,
            Metadata(document),
            DocumentExtraction.Keep(hidden));
    }

    private static void ReadPage(Page page, List<string> lines, List<HiddenTextSpan> hidden)
    {
        var bounds = page.CropBox.Bounds;
        var backgrounds = Backgrounds(page);
        var line = new List<string>();
        double? baseline = null;
        HiddenTextSpan? open = null;

        foreach (var word in page.GetWords())
        {
            if (string.IsNullOrWhiteSpace(word.Text))
            {
                continue;
            }

            var reasons = word.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).Select(l => Hidden(l, bounds, backgrounds)).ToList();
            var reason = reasons.Count > 0 && reasons.Count(r => r is not null) * 2 > reasons.Count
                ? reasons.Where(r => r is not null).GroupBy(r => r).OrderByDescending(g => g.Count()).First().Key
                : null;

            if (reason is { } why)
            {
                open = open is not null && open.Reason == why
                    ? open with { Text = $"{open.Text} {word.Text}" }
                    : Flush(open, hidden) ?? new HiddenTextSpan(word.Text, why, page.Number);
                continue;
            }

            open = Flush(open, hidden);
            var y = word.BoundingBox.Bottom;
            if (baseline is { } previous && Math.Abs(previous - y) > 2)
            {
                lines.Add(string.Join(' ', line));
                line.Clear();
            }

            baseline = y;
            line.Add(word.Text);
        }

        Flush(open, hidden);
        if (line.Count > 0)
        {
            lines.Add(string.Join(' ', line));
        }
    }

    // Adds the open span, if any, and returns null so the caller can start the next one.
    private static HiddenTextSpan? Flush(HiddenTextSpan? open, List<HiddenTextSpan> hidden)
    {
        if (open is not null)
        {
            hidden.Add(open);
        }

        return null;
    }

    private static HiddenTextReason? Hidden(Letter letter, PdfRectangle page, List<PdfRectangle> backgrounds)
    {
        if (letter.RenderingMode == TextRenderingMode.Neither)
        {
            return HiddenTextReason.InvisibleText;
        }

        var centre = Centre(letter.BoundingBox);
        if (!Contains(page, letter.StartBaseLine, tolerance: 1) && !Contains(page, centre, tolerance: 1))
        {
            return HiddenTextReason.OffPage;
        }

        if (letter.PointSize > 0 && letter.PointSize < TinyPointSize)
        {
            return HiddenTextReason.TinyText;
        }

        var colour = letter.RenderingMode is TextRenderingMode.Stroke ? letter.StrokeColor : letter.FillColor;
        if (colour is not null && IsWhite(colour.ToRGBValues()) && !backgrounds.Any(b => Contains(b, centre, tolerance: 0)))
        {
            return HiddenTextReason.WhiteText;
        }

        return null;
    }

    // Anything drawn that text could sit on: filled shapes that are not white, and images.
    private static List<PdfRectangle> Backgrounds(Page page)
    {
        var areas = new List<PdfRectangle>();
        foreach (var path in page.Paths)
        {
            if (path.IsFilled && path.FillColor is { } fill && !IsWhite(fill.ToRGBValues()) && path.GetBoundingRectangle() is { } box)
            {
                areas.Add(box);
            }
        }

        areas.AddRange(page.GetImages().Select(i => i.BoundingBox));
        return areas;
    }

    private static bool IsWhite((double R, double G, double B) rgb) =>
        rgb.R >= WhiteThreshold && rgb.G >= WhiteThreshold && rgb.B >= WhiteThreshold;

    private static PdfPoint Centre(PdfRectangle box) => new((box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2);

    private static bool Contains(PdfRectangle box, PdfPoint point, double tolerance) =>
        point.X >= box.Left - tolerance && point.X <= box.Right + tolerance
        && point.Y >= box.Bottom - tolerance && point.Y <= box.Top + tolerance;

    private static DocumentMetadata Metadata(PdfDocument document)
    {
        var information = document.Information;
        string? creatorTool = null;
        DateTimeOffset? xmpCreated = null;
        if (document.TryGetXmpMetadata(out var xmp))
        {
            var xml = xmp.GetXDocument();
            creatorTool = Element(xml, "CreatorTool");
            xmpCreated = DateTimeOffset.TryParse(Element(xml, "CreateDate"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
        }

        return new DocumentMetadata(
            Clean(information.Producer),
            Clean(information.Creator),
            Clean(creatorTool),
            information.GetCreatedDateTimeOffset()?.ToUniversalTime() ?? xmpCreated,
            information.GetModifiedDateTimeOffset()?.ToUniversalTime());
    }

    private static string? Element(XDocument xml, string localName) =>
        xml.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    internal static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }
}
