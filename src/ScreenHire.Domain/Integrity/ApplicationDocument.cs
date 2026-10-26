using ScreenHire.Domain.Organizations;

namespace ScreenHire.Domain.Integrity;

/// <summary>
/// What content forensics read from one uploaded file (integrity-engine.md §10.10): the text a person sees, the file's
/// own metadata, and any text hidden from people. One row per file and extractor version, written once. The bytes stay in
/// blob storage; this is the employer's record of what the file says, kept as long as the application.
/// </summary>
public sealed class ApplicationDocument : IApplicationScoped
{
    /// <summary>The longest visible text kept, in characters. A resume is a few thousand.</summary>
    public const int MaxTextLength = 60_000;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    /// <summary>The <c>ApplicationFile</c> it was read from.</summary>
    public Guid FileId { get; init; }

    public DocumentKind Kind { get; init; }

    public DocumentExtractionStatus Status { get; init; }

    /// <summary>Bump when what extraction finds changes; a new version reads the file again.</summary>
    public int ExtractorVersion { get; init; }

    /// <summary>
    /// The text a person reading the file sees, in reading order, without hidden text. Untrusted: it is quoted and fenced
    /// whenever it goes near a language model.
    /// </summary>
    public string Text { get; init; } = "";

    public int PageCount { get; init; }

    public DocumentMetadata Metadata { get; init; } = DocumentMetadata.None;

    /// <summary>Text the file hides from people: white, tiny, off the page, or marked hidden.</summary>
    public IReadOnlyList<HiddenTextSpan> HiddenText { get; init; } = [];

    /// <summary>Why nothing was read, for a file that is unsupported, unreadable or skipped.</summary>
    public string? Note { get; init; }

    public DateTimeOffset ExtractedAt { get; init; }
}

/// <summary>What a file is to the application, from the question it answers.</summary>
public enum DocumentKind
{
    Resume,

    /// <summary>A LinkedIn profile the candidate exported to PDF and uploaded themselves. Never fetched (CLAUDE.md 5).</summary>
    LinkedInExport,

    /// <summary>Any other file question, such as a portfolio.</summary>
    Other,
}

public enum DocumentExtractionStatus
{
    Extracted,

    /// <summary>A type with no text extractor (an image, a Word 97 file).</summary>
    Unsupported,

    /// <summary>The file could not be parsed: damaged, encrypted, or too slow to read.</summary>
    Unreadable,

    /// <summary>Not read on purpose: the malware scan found something.</summary>
    Skipped,
}

/// <summary>
/// The file's own description of itself: the tools that made it and when. Every member is what the file claims, which
/// is easy to forge, so it is evidence and never proof.
/// </summary>
/// <remarks>
/// <c>producer</c>: The PDF producer, or the Word document's application. <c>creator</c>: The PDF creator, or the
/// document's author tool. <c>creatorTool</c>: XMP's creator tool, when the file has XMP metadata.
/// </remarks>
public sealed record DocumentMetadata(
    string? Producer = null,
    string? Creator = null,
    string? CreatorTool = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? ModifiedAt = null)
{
    public static DocumentMetadata None { get; } = new();

    /// <summary>Every tool name the file gives, without duplicates.</summary>
    public IEnumerable<string> Tools() =>
        new[] { Producer, Creator, CreatorTool }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A run of text people cannot see, with where it was and why it is invisible.</summary>
public sealed record HiddenTextSpan(string Text, HiddenTextReason Reason, int Page);

public enum HiddenTextReason
{
    /// <summary>White text with nothing darker drawn behind it.</summary>
    WhiteText,

    /// <summary>Smaller than a person can read (under 2 points).</summary>
    TinyText,

    /// <summary>Placed outside the visible page.</summary>
    OffPage,

    /// <summary>Drawn invisibly (PDF rendering mode 3) or marked hidden in a Word document.</summary>
    InvisibleText,
}
