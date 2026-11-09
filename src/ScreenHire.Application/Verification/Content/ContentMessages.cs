using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Messaging;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Documents;
using ScreenHire.Infrastructure.Verification.Llm;
using ScreenHire.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;

namespace ScreenHire.Application.Verification.Content;

/// <summary>
/// Read one uploaded file into an <see cref="ApplicationDocument"/>: its visible text, metadata and hidden text.
/// Cascaded by the malware scan, so a flagged file is recorded as skipped and never parsed. Then the consistency check
/// and the verification pipeline run again, now that there is more to read.
/// </summary>
public sealed record ExtractApplicationDocument(Guid OrganizationId, Guid ApplicationId, Guid FileId) : ITenantMessage;

/// <summary>
/// Run the LLM consistency check for one application once its files are read (verification-engine.md §10.10). Sent after
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

    /// <summary>Deletes the file's blob when a candidate is erased. A blob that is not there is already gone, not an error.</summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>Deletes a reply attachment's blob, from the email attachments container, when a candidate is erased. As <see cref="DeleteAsync"/>.</summary>
    Task DeleteReplyAttachmentAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>The blobs in the uploads container whose name starts with <paramref name="prefix"/>, for the sweep of uploads no application claimed.</summary>
    Task<IReadOnlyList<StoredUploadBlob>> ListAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>A blob in the applicant uploads container: its name and when storage first created it.</summary>
public sealed record StoredUploadBlob(string Name, DateTimeOffset CreatedAt);

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

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteReplyAttachmentAsync(string blobName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredUploadBlob>> ListAsync(string prefix, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredUploadBlob>>([]);
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

        // A file erased with its candidate since the message was sent has nothing left to read: done, not failed.
        if (await db.ApplicationFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == message.FileId, cancellationToken) is not { } file)
        {
            return [];
        }

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
            new AnalyzeApplicationVerification(message.OrganizationId, message.ApplicationId),
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
        LlmAccess deployment,
        OrganizationModels models,
        LlmKeys keys,
        IOptions<VerificationOptions> options,
        TimeProvider time,
        ILogger<CheckApplicationConsistency> logger,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var staleAfter = StaleAfter(options.Value.Llm);
        var check = await db.ConsistencyChecks
            .SingleOrDefaultAsync(c => c.ApplicationId == message.ApplicationId && c.PromptVersion == ConsistencyPrompts.Version, cancellationToken);
        if (check is { Status: not (ConsistencyCheckStatus.Running or ConsistencyCheckStatus.Paused) })
        {
            // Finished already. A redelivery after the outcome was saved may be the only copy of the reassessment that
            // follows it (this handler has no outbox), so ask for it again; the pipeline is idempotent.
            return [new AnalyzeApplicationVerification(message.OrganizationId, message.ApplicationId)];
        }

        // A paused check is asked again: the cap may have been raised, or the month may have changed (ResumePausedAiChecks).
        var resumed = check is { Status: ConsistencyCheckStatus.Paused };
        if (check is { Status: ConsistencyCheckStatus.Running } && now - check.StartedAt < staleAfter)
        {
            // Another delivery is asking the model now. Look again once its claim would be stale.
            return [message.DelayedFor(staleAfter)];
        }

        // An application erased since the message was sent has nothing left to check: done, not failed.
        if (await db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == message.ApplicationId, cancellationToken) is not { } application)
        {
            return [];
        }

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
        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        // ScreenHire's model, or the organization's own key and model.
        var llm = models.AccessFor(settings, deployment, keys);
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

        OutgoingMessages messages = [new AnalyzeApplicationVerification(message.OrganizationId, message.ApplicationId)];
        if (early is null)
        {
            // Every call the check makes is recorded in the usage ledger, and saved with the check's outcome in one save, so a
            // call is recorded once: a redelivery finds the check finished and calls nothing.
            var recorder = new AiUsageRecorder(AiUsageContexts.For(settings, options.Value.Llm, message.OrganizationId, application.Id), time);
            ConsistencyCheckOutcome outcome;
            try
            {
                outcome = await checker.CheckAsync(sources, llm.RecordedBy(recorder), cancellationToken);
            }
#pragma warning disable CA1031 // Rethrown below: the calls already made still cost money, so they are saved before the failure goes on.
            catch
#pragma warning restore CA1031
            {
                db.AiUsageCalls.AddRange(recorder.Calls);
                try
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                }
#pragma warning disable CA1031 // Logged, and the model's failure below is the one that goes on: it is why the check failed.
                catch (Exception saveFailure)
#pragma warning restore CA1031
                {
                    LogUsageNotSaved(logger, saveFailure, application.Id, recorder.Calls.Count);
                }

                throw;
            }

            db.AiUsageCalls.AddRange(recorder.Calls);
            check.Complete(outcome, time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        // Budget shares passed and the cap reached since the last alert, from the ledger. Each message claims its own key.
        if (early is null or { Status: ConsistencyCheckStatus.Paused })
        {
            messages.AddRange(await AiUsageAlertChecks.DueAsync(db, message.OrganizationId, settings, options.Value.Llm, time.GetUtcNow(), cancellationToken));
        }

        if (resumed && check.Status != ConsistencyCheckStatus.Paused)
        {
            // The pipeline ran this analyzer while the check was paused; it reads the check again for the new outcome.
            messages.Add(new RecheckApplicationConsistency(message.OrganizationId, message.ApplicationId, check.CompletedAt?.UtcTicks ?? 0));
        }

        LogChecked(logger, application.Id, check.Status, check.Contradictions.Count);
        return messages;
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
        ScreenHireDbContext db, VerificationSettings? settings, IReadOnlyList<ConsistencySourceText> sources, LlmAccess llm, LlmOptions options,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (settings is not { LlmConsistencyCheckEnabled: true })
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Disabled, "The organization has the consistency check turned off.");
        }

        // The answers checks include this one: with them off, no source is sent to a model.
        if (!settings.CheckAnswers)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Disabled, CheckGroups.TurnedOffNote(CheckGroup.Answers));
        }

        if (sources.Count(s => !string.IsNullOrWhiteSpace(s.Text)) < 2)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.NotEnoughSources, "Fewer than two sources have text to compare.");
        }

        if (llm.Client is null)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Unavailable, llm.UnavailableReason);
        }

        // The cap: an Owner's own, else the deployment's safety net for ScreenHire's key. The AI checks pause; the rule-based checks
        // keep running, and the note says the pause is no result about the candidate. An own key with no cap of its own has none.
        var (cap, unit) = AiUsageLimits.EffectiveCap(settings, options.DefaultMonthlyTokenCap);
        if (cap is not null && AiUsageLimits.CapReached(await db.UsedThisMonthAsync(unit, now, cancellationToken), cap))
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Paused, RunNotes.AiChecksPaused);
        }

        return null;
    }

    private static string? Text(IReadOnlyList<ApplicationDocument> documents, DocumentKind kind) =>
        documents.Where(d => d.Kind == kind && d.Status == DocumentExtractionStatus.Extracted && d.Text.Length > 0)
            .OrderBy(d => d.ExtractedAt)
            .Select(d => d.Text)
            .FirstOrDefault();

    [LoggerMessage(Level = LogLevel.Information, Message = "Consistency check for application {ApplicationId}: {Status}, {Contradictions} contradictions")]
    private static partial void LogChecked(ILogger logger, Guid applicationId, ConsistencyCheckStatus status, int contradictions);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consistency check for application {ApplicationId} failed, and its {Calls} usage rows could not be saved")]
    private static partial void LogUsageNotSaved(ILogger logger, Exception exception, Guid applicationId, int calls);
}
