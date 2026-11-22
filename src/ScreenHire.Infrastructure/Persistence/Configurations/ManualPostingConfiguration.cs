using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class ManualPostingConfiguration : IEntityTypeConfiguration<ManualPosting>
{
    public void Configure(EntityTypeBuilder<ManualPosting> builder)
    {
        builder.Property(p => p.BoardName).HasMaxLength(ManualPosting.BoardNameMaxLength);
        builder.Property(p => p.SourceKey).HasMaxLength(ManualPosting.SourceKeyMaxLength);
        builder.Property(p => p.ListingUrl).HasMaxLength(ManualPosting.ListingUrlMaxLength);
        builder.Property(p => p.PostedContent).HasJsonColumn();
        builder.Ignore(p => p.IsTakenDown);

        // Composite: a posting always belongs to a job of its own organization.
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(p => new { p.OrganizationId, p.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // One link per board on a job: the key is what an application's source names, so two rows with it could not be told apart.
        builder.HasIndex(p => new { p.OrganizationId, p.JobId, p.SourceKey }).IsUnique();
    }
}
