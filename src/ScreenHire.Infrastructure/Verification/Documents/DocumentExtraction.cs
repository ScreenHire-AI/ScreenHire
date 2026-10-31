using System.Text;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Infrastructure.Verification.Documents;

/// <summary>
/// Reads an uploaded file into what content forensics need (verification-engine.md §10.10): the text a person sees, the
/// file's metadata, and text hidden from people. PDF through PdfPig (Apache-2.0), Word 2007+ through
/// DocumentFormat.OpenXml (MIT). Other types are <see cref="DocumentExtractionStatus.Unsupported"/>; a file that fails
/// to parse is <see cref="DocumentExtractionStatus.Unreadable"/>, never an exception, because the bytes are the
/// candidate's and nothing about them should fail the pipeline.
/// </summary>
/// <remarks>
/// The author fields of both formats are left out: they hold a person's name, not a tool, and nothing here needs one.
/// </remarks>
public static class DocumentExtraction
{
    /// <summary>Bump when what extraction finds changes, so files are read again.</summary>
    public const int Version = 1;

    /// <summary>Pages read from one PDF. A resume has one to three; the rest of a long file is not read.</summary>
    public const int MaxPages = 30;

    /// <summary>Hidden spans kept per file, and the longest text kept for one.</summary>
    public const int MaxHiddenSpans = 50;

    public const int MaxHiddenSpanLength = 500;

    /// <summary>How long one file may take to parse.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    /// <summary>Files parsed at once in one process, counting parses abandoned at their deadline that are still running.</summary>
    public const int MaxConcurrentParses = 2;

    // The parsers cannot be stopped mid-page, so an abandoned parse keeps its slot until it really ends. A run of
    // hostile files then queues behind the slots instead of piling up parses on the thread pool.
    private static readonly SemaphoreSlim Parsers = new(MaxConcurrentParses, MaxConcurrentParses);

    /// <summary>
    /// Extracts <paramref name="content"/>, a file named <paramref name="fileName"/>, within <see cref="Deadline"/>. A
    /// parser that is still working when the deadline passes is abandoned and the file recorded as unreadable. The
    /// deadline starts once one of <see cref="MaxConcurrentParses"/> slots is free, so waiting for one never counts.
    /// </summary>
    public static async Task<ExtractedDocument> ExtractAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx"))
        {
            return ExtractedDocument.NotRead(DocumentExtractionStatus.Unsupported, $"No text is read from {DescribeType(extension)} files.");
        }

        await Parsers.WaitAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        // The parsers are synchronous; the task gives the deadline something to wait on, and frees the slot when it ends.
        var token = deadline.Token;
        var parse = Task.Run(() => Extract(content, extension, token), CancellationToken.None);
        _ = parse.ContinueWith(
            ended =>
            {
                _ = ended.Exception; // an abandoned parse's cancellation is observed here, not reported as unobserved
                Parsers.Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            return await parse.WaitAsync(token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ExtractedDocument.NotRead(DocumentExtractionStatus.Unreadable, "The file took too long to read.");
        }
    }

    /// <summary>Extracts synchronously, without a deadline. For tests and callers that bound the time themselves.</summary>
    public static ExtractedDocument Extract(byte[] content, string extension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            return extension switch
            {
                ".pdf" => PdfText.Extract(content, cancellationToken),
                ".docx" => WordText.Extract(content, cancellationToken),
                _ => ExtractedDocument.NotRead(DocumentExtractionStatus.Unsupported, $"No text is read from {DescribeType(extension)} files."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Any parser failure on a candidate's file means "unreadable", whatever the exception type.
        catch (Exception)
#pragma warning restore CA1031
        {
            return ExtractedDocument.NotRead(DocumentExtractionStatus.Unreadable, "The file could not be read. It may be damaged or password-protected.");
        }
    }

    /// <summary>Joins lines, collapses runs of spaces and caps the length at <see cref="ApplicationDocument.MaxTextLength"/>.</summary>
    internal static string Tidy(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            var tidy = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (tidy.Length == 0)
            {
                continue;
            }

            if (text.Length + tidy.Length + 1 > ApplicationDocument.MaxTextLength)
            {
                break;
            }

            text.Append(tidy).Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>Hidden runs as stored: trimmed, at most <see cref="MaxHiddenSpans"/>, each capped.</summary>
    internal static List<HiddenTextSpan> Keep(IEnumerable<HiddenTextSpan> spans) =>
        [.. spans
            .Select(s => s with { Text = s.Text.Trim() })
            .Where(s => s.Text.Length >= 3)
            .Select(s => s.Text.Length <= MaxHiddenSpanLength ? s : s with { Text = s.Text[..MaxHiddenSpanLength] })
            .Take(MaxHiddenSpans)];

    private static string DescribeType(string extension) =>
        extension.Length > 1 ? extension[1..].ToUpperInvariant() : "these";
}

/// <summary>What <see cref="DocumentExtraction"/> read from one file.</summary>
public sealed record ExtractedDocument(
    DocumentExtractionStatus Status,
    string Text,
    int PageCount,
    DocumentMetadata Metadata,
    IReadOnlyList<HiddenTextSpan> HiddenText,
    string? Note = null)
{
    public static ExtractedDocument NotRead(DocumentExtractionStatus status, string note) =>
        new(status, "", 0, DocumentMetadata.None, [], note);
}
