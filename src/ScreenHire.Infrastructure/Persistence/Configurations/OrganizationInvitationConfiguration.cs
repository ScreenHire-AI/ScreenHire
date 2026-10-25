using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.JobAccess).HasConversion<string>().HasMaxLength(20).HasDefaultValue(JobAccess.All).HasSentinel((JobAccess)(-1));
        builder.ToTable(t => t.HasCheckConstraint("ck_organization_invitations_job_access", "job_access IN ('All', 'HiringTeamOnly')"));
        // No foreign key for custom_role_id (ADR-0014): the id is only ever resolved under row-level security, when the
        // invitation is written and again when it is accepted, and a key would let closed invitations keep a role from
        // being deleted forever. Open invitations block a deletion in RoleEndpoints instead.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_organization_invitations_custom_role", "(role = 'Custom') = (custom_role_id IS NOT NULL)"));
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsFixedLength();

        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // Accepting looks the invitation up by its hash, inside the organization the token names.
        builder.HasIndex(i => new { i.OrganizationId, i.TokenHash }).IsUnique();
        // The open invitations list, and a new invitation revoking the address's earlier ones.
        // Not a unique partial index on open invitations: an expired one keeps accepted_at and revoked_at null, so a
        // re-invite after expiry would collide with it. CreateInvitation keeps one open per address under the member lock.
        builder.HasIndex(i => new { i.OrganizationId, i.Email });
    }
}
