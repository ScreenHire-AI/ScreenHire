using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class BrandImageConfiguration : IEntityTypeConfiguration<BrandImage>
{
    public void Configure(EntityTypeBuilder<BrandImage> builder)
    {
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.BlobName).HasMaxLength(300);
        builder.Property(i => i.ContentType).HasMaxLength(20);
        builder.Property(i => i.ScanStatus).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(i => i.IsServable);
        builder.Ignore(i => i.Extension);

        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.OrganizationId, i.Kind });
        // The daily clean-up of abandoned uploads looks for unsaved images by age, one organization at a time.
        builder.HasIndex(i => new { i.OrganizationId, i.UploadedAt }).HasFilter("saved_at is null");
    }
}
