using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class JobVersionConfiguration : IEntityTypeConfiguration<JobVersion>
{
    public void Configure(EntityTypeBuilder<JobVersion> builder)
    {
        builder.Property(v => v.Snapshot).HasJsonColumn();

        // Composite: a version always belongs to a job of its own organization.
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.OrganizationId, v.JobId, v.Version }).IsUnique();
    }
}
