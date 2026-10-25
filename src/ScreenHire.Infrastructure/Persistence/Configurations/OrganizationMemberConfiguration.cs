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
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_organization_members_custom_role", "(role = 'Custom') = (custom_role_id IS NOT NULL)"));

        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        // On (organization_id, id), because foreign key checks bypass row-level security: a member can only hold a role
        // of their own organization. No action: a role somebody has is not deleted (RoleEndpoints refuses it first), and
        // the check waits for the end of the statement, so deleting an organization's members and roles together works.
        builder.HasOne<CustomRole>().WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.CustomRoleId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.NoAction);
        // Finds a user's organizations at sign-in, before a tenant is chosen.
        builder.HasIndex(m => m.UserId);
    }
}
