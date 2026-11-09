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
        // Optimistic concurrency on the xmin system column (no real column): Stripe sends the events of one Checkout within
        // the same second and the webhook handles them in parallel, so one commits and the other is sent again.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class AiUsageChargeConfiguration : IEntityTypeConfiguration<AiUsageCharge>
{
    public void Configure(EntityTypeBuilder<AiUsageCharge> builder)
    {
        builder.ToTable("ai_usage_charges");
        // The id is the ledger call's, assigned by the caller: the primary key is what makes a call settle once.
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.State).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Provider).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Margin).HasPrecision(8, 4);

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The Billing page's "reported so far" and the trial allowance already used.
        builder.HasIndex(c => new { c.OrganizationId, c.State });
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
