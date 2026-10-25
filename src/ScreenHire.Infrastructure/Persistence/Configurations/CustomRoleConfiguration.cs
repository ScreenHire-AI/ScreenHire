using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class CustomRoleConfiguration : IEntityTypeConfiguration<CustomRole>
{
    public void Configure(EntityTypeBuilder<CustomRole> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(CustomRole.MaxNameLength);
        builder.Property(r => r.NormalizedName).HasMaxLength(CustomRole.MaxNameLength);
        builder.Property(r => r.CopiedFrom).HasMaxLength(CustomRole.MaxNameLength);
        // Permission names as a native text[].
        builder.PrimitiveCollection(r => r.Permissions).HasColumnType("text[]").ElementType(e => e.HasConversion<string>());

        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The members' composite foreign key points here, so a member can only hold a role of their own organization.
        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id });
        builder.HasIndex(r => new { r.OrganizationId, r.NormalizedName }).IsUnique();
    }
}
