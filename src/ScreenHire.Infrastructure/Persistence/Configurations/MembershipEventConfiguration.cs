using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class MembershipEventConfiguration : IEntityTypeConfiguration<MembershipEvent>
{
    public void Configure(EntityTypeBuilder<MembershipEvent> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.PreviousRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);

        // No foreign key to users or memberships: the log outlives both (MembershipEvent).
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.OrganizationId, e.OccurredAt });
    }
}
