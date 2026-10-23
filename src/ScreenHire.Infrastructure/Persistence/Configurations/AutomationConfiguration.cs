using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class AutomationRunConfiguration : IEntityTypeConfiguration<AutomationRun>
{
    public void Configure(EntityTypeBuilder<AutomationRun> builder)
    {
        builder.Property(r => r.AutomationId).HasMaxLength(40);
        builder.Property(r => r.AutomationName).HasMaxLength(120);
        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.TriggerStageId).HasMaxLength(40);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Detail).HasMaxLength(1000);
        builder.Property(r => r.Facts).HasJsonColumn();
        builder.Property(r => r.RuleResults).HasJsonColumn();

        // Composite tenant keys: a run belongs to an application and a job of its own organization.
        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id });
        // The engine's idempotency key (workflow-engine.md §3): each automation runs once per event.
        builder.HasIndex(r => new { r.OrganizationId, r.ApplicationId, r.AutomationId, r.EventId }).IsUnique();
        // The automation issues list: blocked and failed runs, newest first.
        builder.HasIndex(r => new { r.OrganizationId, r.Status, r.CreatedAt });
    }
}

internal sealed class AutomationActionRunConfiguration : IEntityTypeConfiguration<AutomationActionRun>
{
    public void Configure(EntityTypeBuilder<AutomationActionRun> builder)
    {
        builder.Property(a => a.Action).HasJsonColumn();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Detail).HasMaxLength(1000);

        // Optimistic concurrency on xmin: if two deliveries of one action race, one commits and the other retries, finds
        // the action finished and does nothing, so a task or a tag is never added twice.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<AutomationRun>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.RunId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.OrganizationId, a.RunId, a.Index }).IsUnique();
    }
}

internal sealed class ApplicationTaskConfiguration : IEntityTypeConfiguration<ApplicationTask>
{
    public void Configure(EntityTypeBuilder<ApplicationTask> builder)
    {
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Note).HasMaxLength(1000);

        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(t => new { t.OrganizationId, t.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // The run that created the task, in the same organization (null for a task a person adds later).
        builder.HasOne<AutomationRun>().WithMany()
            .HasForeignKey(t => new { t.OrganizationId, t.AutomationRunId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.OrganizationId, t.ApplicationId });
        // My tasks: one person's (and the whole team's) open tasks.
        builder.HasIndex(t => new { t.OrganizationId, t.AssigneeUserId, t.CompletedAt });
    }
}
