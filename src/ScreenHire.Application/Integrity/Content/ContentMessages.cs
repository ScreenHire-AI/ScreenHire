using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Integrity;
using ScreenHire.Infrastructure.Integrity;
using ScreenHire.Infrastructure.Integrity.Documents;
using ScreenHire.Infrastructure.Integrity.Llm;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Integrity.Content;

/// <summary>
/// Read one uploaded file into an <see cref="ApplicationDocument"/>: its visible text, metadata and hidden text.
/// Cascaded by the malware scan, so a flagged file is recorded as skipped and never parsed. Then the consistency check
/// and the integrity pipeline run again, now that there is more to read.
/// </summary>
public sealed record ExtractApplicationDocument(Guid OrganizationId, Guid ApplicationId, Guid FileId) : ITenantMessage;

/// <summary>
/// Run the LLM consistency check for one application once its files are read (integrity-engine.md §10.10). Sent after
/// submission and after each file is read; it does nothing until every file has a document, and once the check has an
/// outcome it only asks for the reassessment again.
/// </summary>
public sealed record CheckApplicationConsistency(Guid OrganizationId, Guid ApplicationId) : ITenantMessage;

/// <summary>
/// Reads an uploaded file's bytes from the applicant uploads container. The worker registers the blob storage version;
/// <see cref="NoApplicantFileStore"/> stands in where no storage is configured.
/// </summary>
public interface IApplicantFileStore
{
    /// <summary>The file's bytes, or why they were not read: no storage configured, or larger than <paramref name="maxBytes"/>.</summary>
    Task<StoredFile> ReadAsync(string blobName, long maxBytes, CancellationToken cancellationToken);
}

/// <summary>A file's bytes, or the reason (<see cref="Problem"/>) there are none.</summary>
public sealed record StoredFile(byte[]? Content, string? Problem = null)
{
    public static StoredFile NotRead(string problem) => new(null, problem);
}

/// <summary>The local adapter with no storage: every file is recorded as not read, with the reason.</summary>
public sealed class NoApplicantFileStore : IApplicantFileStore
{
    public Task<StoredFile> ReadAsync(string blobName, long maxBytes, CancellationToken cancellationToken) =>
        Task.FromResult(StoredFile.NotRead("No file storage is configured, so the file was not read."));
}

public static partial class ExtractApplicationDocumentHandler
{
    /// <summary>The largest file read. The careers form's own limit is lower; this guards the worker against anything else.</summary>
    public const long MaxBytes = 25 * 1024 * 1024;

    public static async Task<OutgoingMessages> Handle(
        ExtractApplicationDocument message,
        ScreenHireDbContext db,
        IApplicantFileStore files,
        TimeProvider time,
        ILogger<ExtractApplicationDocument> logger,
        CancellationToken cancellationToken)
    {
        if (await db.ApplicationDocuments.AnyAsync(d => d.FileId == message.FileId && d.ExtractorVersion == DocumentExtraction.Version, cancellationToken))
        {
            return []; // read already: a redelivered message
        }

        var file = await db.ApplicationFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == message.FileId, cancellationToken)
            ?? throw new InvalidOperationException($"File {message.FileId} is not visible to organization {message.OrganizationId}.");
        ExtractedDocument extracted;
        if (file.ScanStatus == MalwareScanStatus.Infected)
        {
            extracted = ExtractedDocument.NotRead(DocumentExtractionStatus.Skipped, "The malware scan flagged this file, so it was not read.");
        }
        else
        {
            var stored = await files.ReadAsync(file.BlobName, MaxBytes, cancellationToken);
            extracted = stored.Content is { } content
                ? await DocumentExtraction.ExtractAsync(content, file.FileName, cancellationToken)
                : ExtractedDocument.NotRead(DocumentExtractionStatus.Unreadable, stored.Problem ?? "The file was not read.");
        }

        db.ApplicationDocuments.Add(new ApplicationDocument
        {
            OrganizationId = file.OrganizationId,
            ApplicationId = file.ApplicationId,
            FileId = file.Id,
            Kind = ApplicationText.KindOf(file.QuestionId),
            Status = extracted.Status,
            ExtractorVersion = DocumentExtraction.Version,
            Text = extracted.Text,
            PageCount = extracted.PageCount,
            Metadata = extracted.Metadata,
            HiddenText = extracted.HiddenText,
            Note = extracted.Note,
            ExtractedAt = time.GetUtcNow(),
        });
        LogExtracted(logger, file.Id, file.ApplicationId, extracted.Status, extracted.HiddenText.Count);
        return
        [
            new CheckApplicationConsistency(message.OrganizationId, message.ApplicationId),
            new AnalyzeApplicationIntegrity(message.OrganizationId, message.ApplicationId),
        ];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "File {FileId} of application {ApplicationId} read: {Status}, {HiddenSpans} hidden passages")]
    private static partial void LogExtracted(ILogger logger, Guid fileId, Guid applicationId, DocumentExtractionStatus status, int hiddenSpans);
}

/// <summary>
/// The consistency check's handler. Not transactional on purpose: the model can take a minute, and no database
/// transaction stays open while it works. Idempotent through the check row instead: the row is claimed (unique per
/// application and prompt version) before the model is called and completed after, so a redelivered message never
/// calls the model twice. A claim left by a worker that stopped mid-call is taken over once it is stale.
/// </summary>
public static partial class CheckApplicationConsistencyHandler
{
    [NonTransactional]
    public static async Task<OutgoingMessages> Handle(
        CheckApplicationConsistency message,
        ScreenHireDbContext db,
        ConsistencyChecker checker,
        LlmAccess llm,
        IOptions<IntegrityOptions> options,
        TimeProvider time,
        ILogger<CheckApplicationConsistency> logger,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var staleAfter = StaleAfter(options.Value.Llm);
        var check = await db.ConsistencyChecks
            .SingleOrDefaultAsync(c => c.ApplicationId == message.ApplicationId && c.PromptVersion == ConsistencyPrompts.Version, cancellationToken);
        if (check is { Status: not ConsistencyCheckStatus.Running })
        {
            // Finished already. A redelivery after the outcome was saved may be the only copy of the reassessment that
            // follows it (this handler has no outbox), so ask for it again; the pipeline is idempotent.
            return [new AnalyzeApplicationIntegrity(message.OrganizationId, message.ApplicationId)];
        }

        if (check is not null && now - check.StartedAt < staleAfter)
        {
            // Another delivery is asking the model now. Look again once its claim would be stale.
            return [message.DelayedFor(staleAfter)];
        }

        var application = await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken)
            ?? throw new InvalidOperationException($"Application {message.ApplicationId} is not visible to organization {message.OrganizationId}.");
        var fileIds = await db.ApplicationFiles.AsNoTracking().Where(f => f.ApplicationId == application.Id).Select(f => f.Id).ToListAsync(cancellationToken);
        var documents = await db.ApplicationDocuments.AsNoTracking()
            .Where(d => d.ApplicationId == application.Id && d.ExtractorVersion == DocumentExtraction.Version)
            .ToListAsync(cancellationToken);
        if (fileIds.Any(id => documents.All(d => d.FileId != id)))
        {
            return []; // a file is still being read; reading it sends this message again
        }

        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, cancellationToken);
        var sources = Sources(documents, ApplicationText.AnswersSource(job, application.Answers));
        var settings = await db.IntegritySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var early = await EarlyOutcomeAsync(db, settings, sources, llm, options.Value.Llm, now, cancellationToken);

        if (check is null)
        {
            check = ConsistencyCheck.Claim(application.OrganizationId, application.Id, ConsistencyPrompts.Version, now);
            db.ConsistencyChecks.Add(check);
        }
        else
        {
            check.Reclaim(now);
        }

        if (early is not null)
        {
            check.Complete(early, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.IsUniqueViolation())
        {
            return []; // another delivery claimed it first, and will finish it
        }

        if (early is null)
        {
            var outcome = await checker.CheckAsync(sources, cancellationToken);
            check.Complete(outcome, time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        LogChecked(logger, application.Id, check.Status, check.Contradictions.Count);
        return [new AnalyzeApplicationIntegrity(message.OrganizationId, message.ApplicationId)];
    }

    /// <summary>
    /// How old a claim must be before another delivery takes it over: past the longest a check can take, every call and
    /// every retry timing out, plus a minute.
    /// </summary>
    public static TimeSpan StaleAfter(LlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Timeout * (ConsistencyChecker.MaxCalls * (LlmAccess.MaxRetries + 1)) + TimeSpan.FromMinutes(1);
    }

    /// <summary>The sources to compare: the answers, the resume and an uploaded LinkedIn export, visible text only.</summary>
    public static IReadOnlyList<ConsistencySourceText> Sources(IReadOnlyList<ApplicationDocument> documents, string answers)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var sources = new List<ConsistencySourceText>();
        if (Text(documents, DocumentKind.Resume) is { } resume)
        {
            sources.Add(new ConsistencySourceText(ConsistencySource.Resume, resume));
        }

        if (!string.IsNullOrWhiteSpace(answers))
        {
            sources.Add(new ConsistencySourceText(ConsistencySource.Answers, answers));
        }

        if (Text(documents, DocumentKind.LinkedInExport) is { } linkedIn)
        {
            sources.Add(new ConsistencySourceText(ConsistencySource.LinkedInExport, linkedIn));
        }

        return sources;
    }

    // The outcome when the model is not asked at all, or null when it should be.
    private static async Task<ConsistencyCheckOutcome?> EarlyOutcomeAsync(
        ScreenHireDbContext db, IntegritySettings? settings, IReadOnlyList<ConsistencySourceText> sources, LlmAccess llm, LlmOptions options,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (settings is not { LlmConsistencyCheckEnabled: true })
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Disabled, "The organization has the consistency check turned off.");
        }

        if (sources.Count(s => !string.IsNullOrWhiteSpace(s.Text)) < 2)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.NotEnoughSources, "Fewer than two sources have text to compare.");
        }

        if (llm.Client is null)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Unavailable, llm.UnavailableReason);
        }

        var used = await db.TokensUsedThisMonthAsync(now, cancellationToken);
        var budget = settings.MonthlyTokenBudget ?? options.DefaultMonthlyTokenBudget;
        return used >= budget
            ? new ConsistencyCheckOutcome(ConsistencyCheckStatus.OverBudget, "The organization's monthly budget for the consistency check is used up.")
            : null;
    }

    private static string? Text(IReadOnlyList<ApplicationDocument> documents, DocumentKind kind) =>
        documents.Where(d => d.Kind == kind && d.Status == DocumentExtractionStatus.Extracted && d.Text.Length > 0)
            .OrderBy(d => d.ExtractedAt)
            .Select(d => d.Text)
            .FirstOrDefault();

    [LoggerMessage(Level = LogLevel.Information, Message = "Consistency check for application {ApplicationId}: {Status}, {Contradictions} contradictions")]
    private static partial void LogChecked(ILogger logger, Guid applicationId, ConsistencyCheckStatus status, int contradictions);
}
