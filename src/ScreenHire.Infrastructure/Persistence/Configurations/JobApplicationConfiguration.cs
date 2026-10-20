using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.Property(a => a.CurrentStageId).HasMaxLength(40);
        builder.Property(a => a.AiUseDeclaration).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AiUseDetails).HasMaxLength(2000);
        builder.Property(a => a.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Answers).HasJsonColumn();
        builder.Property(a => a.PrivacyNoticeVersion).HasMaxLength(40);
        builder.Property(a => a.BotCheck).HasConversion<string>().HasMaxLength(20);

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
