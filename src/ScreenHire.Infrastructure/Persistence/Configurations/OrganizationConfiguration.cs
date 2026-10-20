using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(o => o.Name).HasMaxLength(200);
        builder.Property(o => o.Slug).HasMaxLength(63);
        builder.Property(o => o.HomeRegion).HasMaxLength(16);
        builder.Property(o => o.BrandColor).HasMaxLength(7);
        builder.HasIndex(o => o.Slug).IsUnique();
    }
}
