using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.Property(j => j.Title).HasMaxLength(200);
        builder.Property(j => j.Slug).HasMaxLength(120);
        builder.Property(j => j.Location).HasMaxLength(200);
        builder.Property(j => j.SalaryCurrency).HasMaxLength(3);
        builder.Property(j => j.SalaryMin).HasPrecision(12, 2);
        builder.Property(j => j.SalaryMax).HasPrecision(12, 2);

        // Enums as text: readable in SQL and stable when members are reordered.
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.WorkplaceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.EmploymentType).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Organization>().WithMany().HasForeignKey(j => j.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(j => new { j.OrganizationId, j.Slug }).IsUnique();
        builder.HasIndex(j => new { j.OrganizationId, j.Status });
    }
}
