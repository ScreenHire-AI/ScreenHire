using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Distribution;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class SandboxListingConfiguration : IEntityTypeConfiguration<SandboxListing>
{
    public void Configure(EntityTypeBuilder<SandboxListing> builder)
    {
        builder.ToTable("sandbox_listings");
        builder.Property(l => l.Title).HasMaxLength(200);
        builder.Property(l => l.Url).HasMaxLength(2048);

        builder.HasOne<Organization>().WithMany().HasForeignKey(l => l.OrganizationId).OnDelete(DeleteBehavior.Cascade);

        // The sandbox is a stand-in for another company's database, so it links to the posting by id only, like a real
        // board would; a unique posting id is its idempotency key.
        builder.HasIndex(l => new { l.OrganizationId, l.PostingId }).IsUnique();
    }
}
