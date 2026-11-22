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

internal sealed class CreditEntryConfiguration : IEntityTypeConfiguration<CreditEntry>
{
    public void Configure(EntityTypeBuilder<CreditEntry> builder)
    {
        builder.ToTable("credit_entries");
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SourceKey).HasMaxLength(CreditEntry.MaxSourceKeyLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The effect check (ADR-0032): one grant per purchase, one debit per call, one expiry per grant, however often a message arrives.
        builder.HasIndex(e => new { e.OrganizationId, e.SourceKey }).IsUnique();
        // The deductions list, grouped by the check that used them, newest first.
        builder.HasIndex(e => new { e.OrganizationId, e.Kind, e.At });
    }
}

internal sealed class CreditPurchaseConfiguration : IEntityTypeConfiguration<CreditPurchase>
{
    public void Configure(EntityTypeBuilder<CreditPurchase> builder)
    {
        builder.ToTable("credit_purchases");
        builder.Property(p => p.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(CreditPurchase.MaxProviderIdLength);
        builder.Property(p => p.FailureCode).HasMaxLength(CreditPurchase.MaxFailureCodeLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.OrganizationId, p.CreatedAt });
        // At most one auto-reload waiting for its payment, so checks that finish together cannot charge the card twice. A Checkout left
        // unpaid stays Pending for good, so the guard counts auto-reloads only.
        builder.HasIndex(p => p.OrganizationId).IsUnique().HasFilter("source = 'AutoReload' AND status = 'Pending'")
            .HasDatabaseName("ix_credit_purchases_one_pending_reload");
    }
}

internal sealed class CreditSettingsConfiguration : IEntityTypeConfiguration<CreditSettings>
{
    public void Configure(EntityTypeBuilder<CreditSettings> builder)
    {
        builder.ToTable("credit_settings");
        builder.Property(s => s.LowBalanceAlert).HasPrecision(12, 2);
        builder.Property(s => s.AutoReloadBelow).HasPrecision(12, 2);
        builder.Property(s => s.AutoReloadAmount).HasPrecision(12, 2);
        builder.Property(s => s.AutoReloadMonthlyMax).HasPrecision(12, 2);
        builder.Property(s => s.PaymentMethodId).HasMaxLength(CreditSettings.MaxPaymentMethodLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.OrganizationId).IsUnique();
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
