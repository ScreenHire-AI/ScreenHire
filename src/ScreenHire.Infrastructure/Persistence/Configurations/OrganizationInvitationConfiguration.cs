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
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsFixedLength();

        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // Accepting looks the invitation up by its hash, inside the organization the token names.
        builder.HasIndex(i => new { i.OrganizationId, i.TokenHash }).IsUnique();
        // The open invitations list, and a new invitation revoking the address's earlier ones.
        builder.HasIndex(i => new { i.OrganizationId, i.Email });
    }
}
