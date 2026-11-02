using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class CalendarConnectionConfiguration : IEntityTypeConfiguration<CalendarConnection>
{
    public void Configure(EntityTypeBuilder<CalendarConnection> builder)
    {
        builder.Property(c => c.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.AccountEmail).HasMaxLength(CalendarConnection.MaxAccountEmailLength);
        builder.Property(c => c.ProtectedAccessToken).HasMaxLength(CalendarConnection.MaxTokenLength);
        builder.Property(c => c.ProtectedRefreshToken).HasMaxLength(CalendarConnection.MaxTokenLength);
        builder.Property(c => c.Problem).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // One calendar per member.
        builder.HasIndex(c => new { c.OrganizationId, c.UserId }).IsUnique();
    }
}

internal sealed class CalendarSandboxBusyConfiguration : IEntityTypeConfiguration<CalendarSandboxBusy>
{
    public void Configure(EntityTypeBuilder<CalendarSandboxBusy> builder)
    {
        builder.ToTable("calendar_sandbox_busy");
        builder.HasOne<Organization>().WithMany().HasForeignKey(b => b.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => new { b.OrganizationId, b.UserId, b.StartsAt });
    }
}

internal sealed class CalendarSandboxEventConfiguration : IEntityTypeConfiguration<CalendarSandboxEvent>
{
    public void Configure(EntityTypeBuilder<CalendarSandboxEvent> builder)
    {
        builder.Property(e => e.ProviderEventId).HasMaxLength(CalendarSandboxEvent.MaxEventIdLength);
        builder.Property(e => e.Summary).HasMaxLength(CalendarSandboxEvent.MaxTextLength);
        builder.Property(e => e.Description).HasMaxLength(CalendarSandboxEvent.MaxTextLength);
        builder.Property(e => e.MeetingUrl).HasMaxLength(CalendarSandboxEvent.MaxUrlLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The idempotency check: one event per id in a member's calendar.
        builder.HasIndex(e => new { e.OrganizationId, e.UserId, e.ProviderEventId }).IsUnique();
        // The free/busy query.
        builder.HasIndex(e => new { e.OrganizationId, e.UserId, e.StartsAt });
    }
}
