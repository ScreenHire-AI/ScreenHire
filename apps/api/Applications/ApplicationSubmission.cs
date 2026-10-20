using Microsoft.EntityFrameworkCore;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ScreenHire.Api.Applications;

/// <summary>
/// Creates an application, for a recruiter adding one by hand and for a candidate applying on the careers page. The
/// candidate is found by email within the job's organization, or created. The application starts in the job's first
/// stage; its Submitted timeline event, its files, <see cref="ApplicationSubmitted"/> and one
/// <see cref="ScanApplicationFile"/> per file commit together through the outbox.
/// </summary>
internal static class ApplicationSubmission
{
    /// <summary>The new application and its candidate, or null when the candidate has already applied to the job.</summary>
    /// <remarks>
    /// An existing candidate's name and phone are left as they are. Anyone can type any email address into a careers
    /// form, so a submission never changes the details of a candidate who already exists.
    /// </remarks>
    public static async Task<(JobApplication Application, Candidate Candidate)?> SubmitAsync(
        IDbContextOutbox<ScreenHireDbContext> outbox,
        Job job,
        CandidateInput input,
        ApplicationDeclarations declarations,
        ApplicationIntake intake,
        IReadOnlyList<VerifiedUpload> files,
        DateTimeOffset now,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var db = outbox.DbContext;
        var email = Candidate.Normalize(input.Email);
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.NormalizedEmail == email, cancellationToken);
        if (candidate is null)
        {
            candidate = Candidate.Create(job.OrganizationId, input.FullName, input.Email, input.Phone, now);
            db.Candidates.Add(candidate);
        }
        else if (await db.Applications.AnyAsync(a => a.JobId == job.Id && a.CandidateId == candidate.Id, cancellationToken))
        {
            return null;
        }

        var (application, submitted) = JobApplication.Submit(
            job, candidate, declarations.AccommodationRequested, declarations.AiUseDeclaration, declarations.AiUseDetails,
            now, actorUserId, intake);
        db.Applications.Add(application);
        db.ApplicationEvents.Add(submitted);
        await outbox.PublishAsync(new ApplicationSubmitted(
            application.OrganizationId, application.Id, job.Id, application.CurrentStageId, submitted.Id, now));

        foreach (var upload in files)
        {
            var file = new ApplicationFile
            {
                OrganizationId = application.OrganizationId,
                ApplicationId = application.Id,
                QuestionId = upload.QuestionId,
                UploadId = upload.UploadId,
                BlobName = upload.BlobName,
                FileName = upload.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.SizeBytes,
                Sha256 = upload.Sha256,
                UploadedAt = now,
            };
            db.ApplicationFiles.Add(file);
            await outbox.PublishAsync(new ScanApplicationFile(file.OrganizationId, application.Id, file.Id));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return null; // the same candidate applied to the same job at the same moment
        }

        return (application, candidate);
    }
}

/// <summary>What the candidate told us about themselves: the assistive-technology switch and their AI-use answer.</summary>
internal sealed record ApplicationDeclarations(bool AccommodationRequested, AiUseDeclaration AiUseDeclaration, string? AiUseDetails);
