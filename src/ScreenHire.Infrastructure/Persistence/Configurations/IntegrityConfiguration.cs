using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Integrity;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class IntegrityAnalyzerRunConfiguration : IEntityTypeConfiguration<IntegrityAnalyzerRun>
{
    public void Configure(EntityTypeBuilder<IntegrityAnalyzerRun> builder)
    {
        builder.Property(r => r.Analyzer).HasMaxLength(40);
        builder.Property(r => r.Note).HasMaxLength(500);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id });
        // The pipeline's idempotency key (integrity-engine.md §5): each analyzer version runs once per application.
        builder.HasIndex(r => new { r.OrganizationId, r.ApplicationId, r.Analyzer, r.AnalyzerVersion }).IsUnique();
    }
}

internal sealed class IntegritySignalConfiguration : IEntityTypeConfiguration<IntegritySignal>
{
    public void Configure(EntityTypeBuilder<IntegritySignal> builder)
    {
        builder.Property(s => s.Code).HasMaxLength(40);
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Assessment).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Weight).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Confidence).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Source).HasMaxLength(40);
        builder.Property(s => s.Explanation).HasMaxLength(1000);
        builder.Property(s => s.Evidence).HasJsonColumn();
        builder.Property(s => s.CounterEvidence).HasJsonColumn();
        builder.Property(s => s.Llm).HasJsonColumn();

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IntegrityAnalyzerRun>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.AnalyzerRunId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.OrganizationId, s.ApplicationId });
        builder.HasIndex(s => new { s.OrganizationId, s.AnalyzerRunId });
    }
}

internal sealed class IntegrityAssessmentConfiguration : IEntityTypeConfiguration<IntegrityAssessment>
{
    public void Configure(EntityTypeBuilder<IntegrityAssessment> builder)
    {
        builder.Ignore(a => a.IsRaised);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Level).HasMaxLength(30);
        builder.Property(a => a.RulesetVersion).HasMaxLength(20);
        builder.Property(a => a.ReasonCodes).HasColumnType("text[]");
        builder.Property(a => a.SignalIds).HasColumnType("uuid[]");
        builder.Property(a => a.CountedSignalIds).HasColumnType("uuid[]");
        builder.Property(a => a.AppliedRules).HasColumnType("text[]");
        builder.Property(a => a.Summary).HasMaxLength(2000);
        builder.Property(a => a.NextStep).HasMaxLength(1000);
        builder.Property(a => a.AiUsePolicy).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.AiUseDeclaration).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AiPolicyContext).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Fingerprint).HasMaxLength(64);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(a => new { a.OrganizationId, a.Id });
        // Identical inputs make one row however often the pipeline runs; the newest row per kind is current.
        builder.HasIndex(a => new { a.OrganizationId, a.ApplicationId, a.Kind, a.Fingerprint }).IsUnique();
        builder.HasIndex(a => new { a.OrganizationId, a.ApplicationId, a.Kind, a.AssessedAt });
    }
}

internal sealed class IntegrityReviewActionConfiguration : IEntityTypeConfiguration<IntegrityReviewAction>
{
    public void Configure(EntityTypeBuilder<IntegrityReviewAction> builder)
    {
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DismissReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Note).HasMaxLength(IntegrityReviewAction.MaxNoteLength);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IntegrityAssessment>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.AssessmentId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.OrganizationId, a.ApplicationId, a.OccurredAt });
        builder.HasIndex(a => new { a.OrganizationId, a.AssessmentId });
        // One dismissal per assessment, even when two reviewers press the button at once.
        builder.HasIndex(a => new { a.OrganizationId, a.AssessmentId }, "ix_integrity_review_actions_one_dismissal")
            .IsUnique().HasFilter("type = 'Dismissed'").HasDatabaseName("ix_integrity_review_actions_one_dismissal");
    }
}

internal sealed class SubmissionObservationConfiguration : IEntityTypeConfiguration<SubmissionObservation>
{
    public void Configure(EntityTypeBuilder<SubmissionObservation> builder)
    {
        builder.Property(o => o.IpAddress).HasMaxLength(45);
        builder.Property(o => o.UserAgent).HasMaxLength(SubmissionObservation.MaxUserAgentLength);
        builder.Property(o => o.DeviceHash).HasMaxLength(64);
        builder.Property(o => o.AutomationMarkers).HasColumnType("text[]");

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(o => new { o.OrganizationId, o.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => new { o.OrganizationId, o.ApplicationId }).IsUnique();
        // Within-organization duplicate checks find other applications from the same device.
        builder.HasIndex(o => new { o.OrganizationId, o.DeviceHash });
        builder.HasIndex(o => new { o.OrganizationId, o.ExpiresAt });
    }
}

internal sealed class TelemetrySnapshotConfiguration : IEntityTypeConfiguration<TelemetrySnapshot>
{
    public void Configure(EntityTypeBuilder<TelemetrySnapshot> builder)
    {
        builder.Property(s => s.Fields).HasJsonColumn();
        builder.Property(s => s.Page).HasJsonColumn();

        builder.HasOne<Job>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // One row per page view: a later snapshot of the same page view replaces it (an upsert on this index).
        builder.HasIndex(s => new { s.OrganizationId, s.SessionId, s.PageViewId }).IsUnique();
        builder.HasIndex(s => new { s.OrganizationId, s.ApplicationId });
        builder.HasIndex(s => new { s.OrganizationId, s.ExpiresAt });
    }
}

internal sealed class ApplicationDocumentConfiguration : IEntityTypeConfiguration<ApplicationDocument>
{
    public void Configure(EntityTypeBuilder<ApplicationDocument> builder)
    {
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Text).HasMaxLength(ApplicationDocument.MaxTextLength);
        builder.Property(d => d.Metadata).HasJsonColumn();
        builder.Property(d => d.HiddenText).HasJsonColumn();
        builder.Property(d => d.Note).HasMaxLength(500);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(d => new { d.OrganizationId, d.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationFile>().WithMany()
            .HasForeignKey(d => new { d.OrganizationId, d.FileId })
            .HasPrincipalKey(f => new { f.OrganizationId, f.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // Each extractor version reads a file once, however often the message arrives.
        builder.HasIndex(d => new { d.OrganizationId, d.FileId, d.ExtractorVersion }).IsUnique();
        builder.HasIndex(d => new { d.OrganizationId, d.ApplicationId });
    }
}

internal sealed class AnswerFingerprintConfiguration : IEntityTypeConfiguration<AnswerFingerprint>
{
    public void Configure(EntityTypeBuilder<AnswerFingerprint> builder)
    {
        builder.Property(f => f.QuestionId).HasMaxLength(40);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(f => new { f.OrganizationId, f.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => new { f.OrganizationId, f.ApplicationId, f.QuestionId }).IsUnique();
        // Near-duplicate lookups read an organization's recent fingerprints, newest first.
        builder.HasIndex(f => new { f.OrganizationId, f.CreatedAt });
    }
}

internal sealed class ConsistencyCheckConfiguration : IEntityTypeConfiguration<ConsistencyCheck>
{
    public void Configure(EntityTypeBuilder<ConsistencyCheck> builder)
    {
        builder.Property(c => c.PromptVersion).HasMaxLength(40);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(500);
        builder.Property(c => c.ExtractionModel).HasMaxLength(100);
        builder.Property(c => c.ConsistencyModel).HasMaxLength(100);
        builder.Property(c => c.Contradictions).HasJsonColumn();
        // A claim is taken over only if nobody else took it first (ConsistencyCheck.Reclaim).
        builder.Property(c => c.StartedAt).IsConcurrencyToken();

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // The claim: one check per application and prompt version, so a redelivered message never calls the model twice.
        builder.HasIndex(c => new { c.OrganizationId, c.ApplicationId, c.PromptVersion }).IsUnique();
        // The monthly budget sums an organization's checks by start time.
        builder.HasIndex(c => new { c.OrganizationId, c.StartedAt });
    }
}

internal sealed class IntegritySettingsConfiguration : IEntityTypeConfiguration<IntegritySettings>
{
    public void Configure(EntityTypeBuilder<IntegritySettings> builder)
    {
        builder.ToTable("integrity_settings");
        builder.HasKey(s => s.OrganizationId);
        builder.HasOne<Organization>().WithOne()
            .HasForeignKey<IntegritySettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SignalNetworkEntryConfiguration : IEntityTypeConfiguration<SignalNetworkEntry>
{
    public void Configure(EntityTypeBuilder<SignalNetworkEntry> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.IdentifierHash).HasMaxLength(64);
        builder.Property(e => e.ContributorHash).HasMaxLength(64);
        builder.Property(e => e.KeyId).HasMaxLength(40);

        builder.HasIndex(e => new { e.Kind, e.IdentifierHash, e.SeenAt });
        builder.HasIndex(e => e.ExpiresAt);
    }
}
