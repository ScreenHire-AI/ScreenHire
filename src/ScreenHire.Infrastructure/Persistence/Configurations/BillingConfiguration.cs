using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Billing;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class BillingAccountConfiguration : IEntityTypeConfiguration<BillingAccount>
{
    public void Configure(EntityTypeBuilder<BillingAccount> builder)
    {
        builder.Property(a => a.PlanKey).HasMaxLength(BillingAccount.MaxKeyLength);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ProviderCustomerId).HasMaxLength(BillingAccount.MaxProviderIdLength);
        builder.Property(a => a.ProviderSubscriptionId).HasMaxLength(BillingAccount.MaxProviderIdLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // One account per organization, so two requests starting a trial at once cannot both commit.
        builder.HasIndex(a => a.OrganizationId).IsUnique();
    }
}

internal sealed class BillingEventRecordConfiguration : IEntityTypeConfiguration<BillingEventRecord>
{
    public void Configure(EntityTypeBuilder<BillingEventRecord> builder)
    {
        builder.ToTable("billing_events");
        builder.Property(e => e.EventId).HasMaxLength(BillingEventRecord.MaxEventIdLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The effect check: one record per provider event, so a redelivery racing the first delivery cannot apply twice.
        builder.HasIndex(e => new { e.OrganizationId, e.EventId }).IsUnique();
    }
}
