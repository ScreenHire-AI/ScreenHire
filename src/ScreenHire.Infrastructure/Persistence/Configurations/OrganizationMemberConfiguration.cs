using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        // Finds a user's organizations at sign-in, before a tenant is chosen.
        builder.HasIndex(m => m.UserId);
    }
}
