using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Jobs;
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

internal sealed class InterviewRequestConfiguration : IEntityTypeConfiguration<InterviewRequest>
{
    public void Configure(EntityTypeBuilder<InterviewRequest> builder)
    {
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TimeZone).HasMaxLength(InterviewRequest.MaxTimeZoneLength);
        builder.Property(r => r.MeetingUrl).HasMaxLength(InterviewRequest.MaxMeetingUrlLength);
        builder.Property(r => r.SourceKey).HasMaxLength(InterviewRequest.MaxSourceKeyLength);

        // The request goes with its application (and so its job), like the other rows under an application.
        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // The attendee's foreign key names the application too, so a row cannot say it is for another application than its
        // interview (row-level security on the attendee reads its job through that application).
        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id, r.ApplicationId });
        // One live interview per application. The index sees the stored status, so an Open request past its window is
        // stored as Expired before a new one is made.
        builder.HasIndex(r => new { r.OrganizationId, r.ApplicationId }).IsUnique().HasFilter("status IN ('Open', 'Booked')");
        // A workflow action that runs twice finds its request.
        builder.HasIndex(r => new { r.OrganizationId, r.ApplicationId, r.SourceKey }).IsUnique().HasFilter("source_key IS NOT NULL");
        // The overlap check: confirmed bookings in a time range.
        builder.HasIndex(r => new { r.OrganizationId, r.StartsAt }).HasFilter("status = 'Booked'");
    }
}

internal sealed class InterviewAttendeeConfiguration : IEntityTypeConfiguration<InterviewAttendee>
{
    public void Configure(EntityTypeBuilder<InterviewAttendee> builder)
    {
        builder.Property(a => a.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.SyncStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ProviderEventId).HasMaxLength(InterviewAttendee.MaxEventIdLength);
        builder.Property(a => a.SyncProblem).HasMaxLength(InterviewAttendee.MaxProblemLength);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.ApplicationId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InterviewRequest>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.InterviewId, a.ApplicationId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id, r.ApplicationId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.OrganizationId, a.InterviewId, a.UserId }).IsUnique();
        // The overlap check: one interviewer's interviews.
        builder.HasIndex(a => new { a.OrganizationId, a.UserId });
    }
}
