using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class ChangeRecordConfiguration : IEntityTypeConfiguration<ChangeRecord>
{
    public void Configure(EntityTypeBuilder<ChangeRecord> builder)
    {
        builder.Property(r => r.Sequence).UseIdentityAlwaysColumn();
        builder.Property(r => r.SubjectKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.SubjectId).HasMaxLength(64);
        builder.Property(r => r.Lines).HasJsonColumn();

        // No foreign key to users or API keys: the history outlives a removed member or a revoked key.
        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // A subject's history, newest first, and its last change.
        builder.HasIndex(r => new { r.OrganizationId, r.SubjectKind, r.SubjectId, r.Sequence }).IsDescending(false, false, false, true);
    }
}
