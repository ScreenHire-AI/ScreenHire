using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.Property(e => e.Url).HasMaxLength(WebhookEndpoint.MaxUrlLength);
        builder.Property(e => e.Description).HasMaxLength(WebhookEndpoint.MaxDescriptionLength);
        // Event type names as a native text[]; the fan-out matches them with = ANY(events).
        builder.Property(e => e.Events);
        builder.Property(e => e.DisabledReason).HasMaxLength(WebhookEndpoint.MaxDisabledReasonLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The target of webhook_deliveries' composite tenant key.
        builder.HasAlternateKey(e => new { e.OrganizationId, e.Id });
        builder.HasIndex(e => new { e.OrganizationId, e.Enabled });
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.Property(d => d.EventType).HasMaxLength(40);
        // Text, not jsonb: jsonb would reorder and respace the body, and the signature covers the exact bytes sent.
        builder.Property(d => d.Payload);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(WebhookDelivery.MaxErrorLength);

        // Composite: a delivery always belongs to an endpoint of its own organization, and goes with it.
        builder.HasOne<WebhookEndpoint>().WithMany()
            .HasForeignKey(d => new { d.OrganizationId, d.EndpointId })
            .HasPrincipalKey(e => new { e.OrganizationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // One first delivery per event and endpoint, so a redelivered fan-out adds nothing. Replays are extra rows.
        builder.HasIndex(d => new { d.OrganizationId, d.EndpointId, d.EventId }).IsUnique().HasFilter("replay_of_id IS NULL");
        // The delivery log, newest first.
        builder.HasIndex(d => new { d.OrganizationId, d.EndpointId, d.CreatedAt });
    }
}
