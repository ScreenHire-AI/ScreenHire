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
