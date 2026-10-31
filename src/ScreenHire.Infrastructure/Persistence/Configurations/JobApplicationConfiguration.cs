using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.Property(a => a.CurrentStageId).HasMaxLength(40);
        builder.Property(a => a.AiUseDeclaration).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AiUseDetails).HasMaxLength(2000);
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.SourceSite).HasMaxLength(EmbedOrigins.MaxLength);
        builder.Property(a => a.Answers).HasJsonColumn();
        builder.Property(a => a.PrivacyNoticeVersion).HasMaxLength(40);
        builder.Property(a => a.BotCheck).HasConversion<string>().HasMaxLength(20);
        // Existing rows get an empty list; new rows always send their own.
        builder.Property(a => a.Tags).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

        // Optimistic concurrency on Postgres' xmin system column (no real column): two people moving one application at
        // once get one success and one conflict, never a lost update.
        builder.Property<uint>("Version").IsRowVersion();

        // Composite tenant foreign keys: an application can only join a job and a candidate of its own organization.
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Candidate>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.CandidateId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasAlternateKey(a => new { a.OrganizationId, a.Id });
        builder.HasIndex(a => new { a.OrganizationId, a.JobId, a.CandidateId }).IsUnique();
        builder.HasIndex(a => new { a.OrganizationId, a.CandidateId });
        // The pipeline view: one job's applications by stage.
        builder.HasIndex(a => new { a.OrganizationId, a.JobId, a.CurrentStageId });
    }
}

internal sealed class ApplicationEventConfiguration : IEntityTypeConfiguration<ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ApplicationEvent> builder)
    {
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Data).HasJsonColumn();

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.OrganizationId, e.ApplicationId, e.OccurredAt });
    }
}

internal sealed class QuestionnaireRequestConfiguration : IEntityTypeConfiguration<QuestionnaireRequest>
{
    public void Configure(EntityTypeBuilder<QuestionnaireRequest> builder)
    {
        builder.Property(r => r.QuestionnaireId).HasMaxLength(40);
        builder.Property(r => r.QuestionnaireName).HasMaxLength(80);
        builder.Property(r => r.Questions).HasJsonColumn();
        builder.Property(r => r.Answers).HasJsonColumn();
        builder.Property(r => r.DraftAnswers).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
        // Optimistic concurrency on xmin: two answers to one link racing each other give one success and one conflict.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // One request per application and questionnaire: sending again finds it, and two sends racing hit this index.
        builder.HasIndex(r => new { r.OrganizationId, r.ApplicationId, r.QuestionnaireId }).IsUnique();
    }
}

internal sealed class ApplicationDraftConfiguration : IEntityTypeConfiguration<ApplicationDraft>
{
    public void Configure(EntityTypeBuilder<ApplicationDraft> builder)
    {
        builder.Property(d => d.Email).HasMaxLength(320);
        builder.Property(d => d.FirstName).HasMaxLength(Candidate.MaxNamePartLength);
        builder.Property(d => d.LastName).HasMaxLength(Candidate.MaxNamePartLength);
        builder.Property(d => d.Phone).HasMaxLength(50);
        builder.Property(d => d.Answers).HasJsonColumn();
        builder.Property(d => d.AiUseDeclaration).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.AiUseDetails).HasMaxLength(2000);

        builder.HasOne<Job>().WithMany()
            .HasForeignKey(d => new { d.OrganizationId, d.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // Submission finds the job's drafts for the email; the link cap counts an address's drafts of the last day; the
        // daily sweep finds the expired ones.
        builder.HasIndex(d => new { d.OrganizationId, d.JobId, d.Email });
        builder.HasIndex(d => new { d.OrganizationId, d.Email, d.CreatedAt });
        builder.HasIndex(d => new { d.OrganizationId, d.ExpiresAt });
    }
}

internal sealed class ApplicationScorecardConfiguration : IEntityTypeConfiguration<ApplicationScorecard>
{
    public void Configure(EntityTypeBuilder<ApplicationScorecard> builder)
    {
        builder.Property(s => s.StageId).HasMaxLength(40);
        builder.Property(s => s.ScorecardTemplateId).HasMaxLength(40);
        builder.Property(s => s.ScorecardName).HasMaxLength(ScorecardTemplate.MaxNameLength);
        builder.Property(s => s.Ratings).HasJsonColumn();
        builder.Property(s => s.Answers).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
        builder.Property(s => s.Recommendation).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Note).HasMaxLength(ApplicationScorecard.MaxNoteLength);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // The application's own job, in the same organization; set from the application, never from a request.
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // One judgement per reviewer, stage and scorecard: a second submission is the same request again or a conflict,
        // and two submissions racing each other hit this index. Nulls are not distinct, so a stage copy that names no
        // library scorecard (and every scorecard given before submissions kept one) is still one per reviewer and stage.
        builder.HasIndex(s => new { s.OrganizationId, s.ApplicationId, s.StageId, s.ReviewerUserId, s.ScorecardTemplateId })
            .IsUnique()
            .AreNullsDistinct(false);
    }
}

internal sealed class ApplicationFileConfiguration : IEntityTypeConfiguration<ApplicationFile>
{
    public void Configure(EntityTypeBuilder<ApplicationFile> builder)
    {
        builder.Property(f => f.QuestionId).HasMaxLength(40);
        builder.Property(f => f.BlobName).HasMaxLength(400);
        builder.Property(f => f.FileName).HasMaxLength(200);
        builder.Property(f => f.ContentType).HasMaxLength(100);
        builder.Property(f => f.Sha256).HasMaxLength(64);
        builder.Property(f => f.ScanStatus).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(f => new { f.OrganizationId, f.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // Same-organization duplicate checks look files up by content (integrity-engine.md, ID_DUPLICATE_IN_ORG).
        builder.HasIndex(f => new { f.OrganizationId, f.Sha256 });
        // One file answers one question of one application; also the index for an application's files.
        builder.HasIndex(f => new { f.OrganizationId, f.ApplicationId, f.QuestionId }).IsUnique();
    }
}

internal sealed class ApplicationNoteConfiguration : IEntityTypeConfiguration<ApplicationNote>
{
    public void Configure(EntityTypeBuilder<ApplicationNote> builder)
    {
        builder.Property(n => n.Text).HasMaxLength(JobApplication.MaxNoteLength);

        // Two people editing or deleting one note at once: one wins, the other gets a conflict (xmin, as on applications).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(n => new { n.OrganizationId, n.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(n => n.Mentions).WithOne()
            .HasForeignKey(m => new { m.OrganizationId, m.NoteId })
            .HasPrincipalKey(n => new { n.OrganizationId, n.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(n => n.Mentions).AutoInclude();

        builder.HasAlternateKey(n => new { n.OrganizationId, n.Id });
        builder.HasIndex(n => new { n.OrganizationId, n.ApplicationId });
    }
}

internal sealed class ApplicationEmailConfiguration : IEntityTypeConfiguration<ApplicationEmail>
{
    public void Configure(EntityTypeBuilder<ApplicationEmail> builder)
    {
        builder.Property(e => e.TemplateId).HasMaxLength(40);
        builder.Property(e => e.To).HasMaxLength(320);
        builder.Property(e => e.Subject).HasMaxLength(ApplicationEmail.MaxSubjectLength);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.OrganizationId, e.ApplicationId });
    }
}

internal sealed class ApplicationNoteMentionConfiguration : IEntityTypeConfiguration<ApplicationNoteMention>
{
    public void Configure(EntityTypeBuilder<ApplicationNoteMention> builder)
    {
        // A person is mentioned once per note, however often the text names them.
        builder.HasIndex(m => new { m.OrganizationId, m.NoteId, m.UserId }).IsUnique();
    }
}
