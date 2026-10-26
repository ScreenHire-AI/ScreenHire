using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.Property(p => p.BoardId).HasMaxLength(40);
        builder.Property(p => p.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Url).HasMaxLength(2048);
        builder.Property(p => p.ExternalId).HasMaxLength(200);
        builder.Property(p => p.LastError).HasMaxLength(1000);
        // Plain-language issues as a native text[].
        builder.Property(p => p.Issues);
        builder.Ignore(p => p.IsActive);

        // Optimistic concurrency on Postgres' xmin system column (no real column), as on jobs. The API's publish and close
        // take the worker's row lock before loading postings (PostingLocks.BeginForJobAsync), so they plan on a board's
        // latest answer; this check is the backstop that refuses any other write of a stale posting over that answer.
        builder.Property<uint>("Version").IsRowVersion();

        // Composite: a posting always belongs to a job of its own organization.
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(p => new { p.OrganizationId, p.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // The target of indexing_notifications' composite tenant key.
        builder.HasAlternateKey(p => new { p.OrganizationId, p.Id });
        builder.HasIndex(p => new { p.OrganizationId, p.JobId, p.BoardId }).IsUnique();
        // Feeds and the public job-board API list one board's active postings.
        builder.HasIndex(p => new { p.OrganizationId, p.BoardId, p.State });
        // Reconciliation looks for postings due a status check.
        builder.HasIndex(p => new { p.OrganizationId, p.NextCheckAt });
    }
}
