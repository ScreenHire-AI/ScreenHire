using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.Property(n => n.Kind).HasMaxLength(Notification.MaxKindLength);
        builder.Property(n => n.Key).HasMaxLength(Notification.MaxKeyLength);
        builder.Property(n => n.Link).HasMaxLength(Notification.MaxLinkLength);
        builder.Property(n => n.Payload).HasJsonColumn();

        builder.HasOne<Organization>().WithMany().HasForeignKey(n => n.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // A person's notifications go with them.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);

        // Raising one twice for a person is one row; clearing finds every person's copy by kind and key.
        builder.HasIndex(n => new { n.OrganizationId, n.UserId, n.Kind, n.Key }).IsUnique();
        // The bell reads one person's newest first. The daily purge reads one organization by age across its people, which this
        // index serves only by its organization prefix: fine at a month of rows, and the place for an (organization, created)
        // index if that grows.
        builder.HasIndex(n => new { n.OrganizationId, n.UserId, n.CreatedAt });
    }
}
