using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class NamedTeamConfiguration : IEntityTypeConfiguration<NamedTeam>
{
    public void Configure(EntityTypeBuilder<NamedTeam> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(NamedTeam.MaxNameLength);
        builder.Property(t => t.NormalizedName).HasMaxLength(NamedTeam.MaxNameLength);
        // Member ids as a native uuid[], as a job's hiring team holds them (ADR-0015).
        builder.PrimitiveCollection(t => t.MemberIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");

        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.OrganizationId, t.NormalizedName }).IsUnique();
    }
}
