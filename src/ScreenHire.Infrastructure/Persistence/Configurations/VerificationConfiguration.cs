using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class VerificationAnalyzerRunConfiguration : IEntityTypeConfiguration<VerificationAnalyzerRun>
{
    public void Configure(EntityTypeBuilder<VerificationAnalyzerRun> builder)
    {
        builder.Property(r => r.Analyzer).HasMaxLength(40);
        builder.Property(r => r.Note).HasMaxLength(500);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id });
        builder.Property(r => r.Revision).HasDefaultValue(1);
        // The pipeline's idempotency key (verification-engine.md §5): each analyzer version runs once per application. A
        // re-check of the same version (the duplicate check when a later application shares a detail) is the next revision.
        builder.HasIndex(r => new { r.OrganizationId, r.ApplicationId, r.Analyzer, r.AnalyzerVersion, r.Revision }).IsUnique();
    }
}

internal sealed class VerificationSignalConfiguration : IEntityTypeConfiguration<VerificationSignal>
{
    public void Configure(EntityTypeBuilder<VerificationSignal> builder)
    {
        builder.Property(s => s.Code).HasMaxLength(40);
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Assessment).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Weight).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Confidence).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Source).HasMaxLength(40);
        builder.Property(s => s.SourceRevision).HasDefaultValue(1);
        builder.Property(s => s.Explanation).HasMaxLength(1000);
        builder.Property(s => s.Evidence).HasJsonColumn();
        builder.Property(s => s.CounterEvidence).HasJsonColumn();
        builder.Property(s => s.Llm).HasJsonColumn();

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VerificationAnalyzerRun>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.AnalyzerRunId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // Target of the review actions' composite tenant foreign key (a reason explained or undone points at a signal).
        builder.HasAlternateKey(s => new { s.OrganizationId, s.Id });
        builder.HasIndex(s => new { s.OrganizationId, s.ApplicationId });
        builder.HasIndex(s => new { s.OrganizationId, s.AnalyzerRunId });
    }
}

internal sealed class VerificationAssessmentConfiguration : IEntityTypeConfiguration<VerificationAssessment>
{
    public void Configure(EntityTypeBuilder<VerificationAssessment> builder)
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

internal sealed class VerificationReviewActionConfiguration : IEntityTypeConfiguration<VerificationReviewAction>
{
    public void Configure(EntityTypeBuilder<VerificationReviewAction> builder)
    {
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DismissReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.ExplainReason).HasConversion<string>().HasMaxLength(30);
        // SignalId is null for a dismissal or a note, which the foreign key then skips. The endpoint also checks the
        // signal is one of the assessment's.
        builder.Property(a => a.Note).HasMaxLength(VerificationReviewAction.MaxNoteLength);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VerificationAssessment>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.AssessmentId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VerificationSignal>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.SignalId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // A read through an API key (ADR-0021) names the key instead of a person. Cascade like the other keys here: the app
        // role cannot delete a key (revoking keeps the row, so the log stays append-only), and only an organization's own
        // deletion removes one, which NO ACTION would refuse. The key is one column, not (OrganizationId, Id) like the other
        // keys here: the value only ever comes from the authenticated key's own claim, with the organization resolved from
        // that same key, so a row cannot name another organization's key.
        builder.HasOne<ApiKey>().WithMany().HasForeignKey(a => a.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
        // A person or a key, never both. Only an EvidenceChanged entry has neither: the system writes it.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_verification_review_actions_one_actor",
            "((actor_user_id IS NOT NULL) <> (api_key_id IS NOT NULL)) OR (type = 'EvidenceChanged' AND actor_user_id IS NULL AND api_key_id IS NULL)"));
        builder.HasIndex(a => new { a.OrganizationId, a.ApplicationId, a.OccurredAt });
        builder.HasIndex(a => new { a.OrganizationId, a.AssessmentId });
        // One dismissal per assessment, even when two reviewers press the button at once.
        builder.HasIndex(a => new { a.OrganizationId, a.AssessmentId }, "ix_verification_review_actions_one_dismissal")
            .IsUnique().HasFilter("type = 'Dismissed'").HasDatabaseName("ix_verification_review_actions_one_dismissal");
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
        builder.Property(o => o.PageTimes).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
        builder.Property(o => o.CarriedOverQuestionIds).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

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

internal sealed class VerificationSettingsConfiguration : IEntityTypeConfiguration<VerificationSettings>
{
    public void Configure(EntityTypeBuilder<VerificationSettings> builder)
    {
        builder.ToTable("verification_settings");
        builder.HasKey(s => s.OrganizationId);
        builder.Property(s => s.LlmSource).HasConversion<string>().HasMaxLength(20).HasDefaultValue(LlmSource.Included);
        builder.Property(s => s.LlmProvider).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.LlmBaseUrl).HasMaxLength(VerificationSettings.MaxBaseUrlLength);
        builder.Property(s => s.LlmModel).HasMaxLength(VerificationSettings.MaxModelLength);
        builder.Property(s => s.ProtectedLlmApiKey).HasMaxLength(VerificationSettings.MaxProtectedKeyLength);
        builder.Property(s => s.CheckNetwork).HasDefaultValue(true);
        builder.Property(s => s.CheckContact).HasDefaultValue(true);
        builder.Property(s => s.CheckDocuments).HasDefaultValue(true);
        builder.Property(s => s.CheckAnswers).HasDefaultValue(true);
        builder.Property(s => s.CheckDuplicates).HasDefaultValue(true);
        builder.Property(s => s.CheckFormUse).HasDefaultValue(true);
        // The sentinel names the column default: Allowed is the enum's zero, and EF would otherwise read a saved Allowed as
        // "not set" on insert and store AllowedWithDisclosure instead.
        builder.Property(s => s.DefaultAiUse).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(AiUsePolicy.AllowedWithDisclosure).HasSentinel(AiUsePolicy.AllowedWithDisclosure);
        builder.Property(s => s.DeviceCheck).HasConversion<string>().HasMaxLength(20);
        // Native text[], replaced as a whole on each save.
        builder.PrimitiveCollection(s => s.TrustedDomains).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        builder.PrimitiveCollection(s => s.TrustedNetworks).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        // Tokens are whole numbers and dollars have cents, so one decimal column holds either; the unit says which.
        builder.Property(s => s.BudgetUnit).HasConversion<string>().HasMaxLength(10).HasDefaultValue(UsageUnit.Tokens);
        builder.Property(s => s.MonthlyBudget).HasPrecision(18, 2);
        builder.Property(s => s.MonthlyCap).HasPrecision(18, 2);
        builder.Property(s => s.OwnKeyInputPrice).HasPrecision(12, 4);
        builder.Property(s => s.OwnKeyOutputPrice).HasPrecision(12, 4);
        builder.HasOne<Organization>().WithOne()
            .HasForeignKey<VerificationSettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AiUsageCallConfiguration : IEntityTypeConfiguration<AiUsageCall>
{
    public void Configure(EntityTypeBuilder<AiUsageCall> builder)
    {
        builder.ToTable("ai_usage_calls");
        builder.Property(c => c.CheckName).HasMaxLength(AiUsageCall.MaxCheckNameLength);
        builder.Property(c => c.Provider).HasMaxLength(AiUsageCall.MaxProviderLength);
        builder.Property(c => c.Model).HasMaxLength(AiUsageCall.MaxModelLength);
        builder.Property(c => c.KeySource).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.ProviderRequestId).HasMaxLength(AiUsageCall.MaxRequestIdLength);
        builder.Property(c => c.Cost).HasPrecision(18, 8);
        builder.Property(c => c.CostBasis).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.ErrorKind).HasMaxLength(AiUsageCall.MaxErrorKindLength);
        builder.Ignore(c => c.Tokens);

        builder.HasOne<Organization>().WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        // The month's and the day's sums, and the date filter on the call list.
        builder.HasIndex(c => new { c.OrganizationId, c.OccurredAt });
        // The call list pages by id (version 7, so by time), newest first.
        builder.HasIndex(c => new { c.OrganizationId, c.Id });
    }
}

internal sealed class AiUsageAlertConfiguration : IEntityTypeConfiguration<AiUsageAlert>
{
    public void Configure(EntityTypeBuilder<AiUsageAlert> builder)
    {
        builder.ToTable("ai_usage_alerts");
        builder.Property(a => a.Key).HasMaxLength(AiUsageAlert.MaxKeyLength);
        builder.HasOne<Organization>().WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        // The claim: one alert per organization and key.
        builder.HasIndex(a => new { a.OrganizationId, a.Key }).IsUnique();
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
