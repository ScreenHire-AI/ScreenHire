using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Distribution;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class IndexingNotificationConfiguration : IEntityTypeConfiguration<IndexingNotification>
{
    public void Configure(EntityTypeBuilder<IndexingNotification> builder)
    {
        builder.Property(n => n.Url).HasMaxLength(2048);
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(n => n.IsPending);

        // Composite: a notification always belongs to a posting of its own organization.
        builder.HasOne<JobPosting>().WithMany()
            .HasForeignKey(n => new { n.OrganizationId, n.PostingId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.OrganizationId, n.SentAt, n.SupersededAt });
    }
}

internal sealed class IndexingQuotaDayConfiguration : IEntityTypeConfiguration<IndexingQuotaDay>
{
    public void Configure(EntityTypeBuilder<IndexingQuotaDay> builder)
    {
        builder.ToTable("indexing_quota_days");
        builder.HasKey(d => d.Day);
    }
}
