using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Slack;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class SlackInstallationConfiguration : IEntityTypeConfiguration<SlackInstallation>
{
    public void Configure(EntityTypeBuilder<SlackInstallation> builder)
    {
        builder.Property(i => i.TeamId).HasMaxLength(SlackInstallation.MaxIdLength);
        builder.Property(i => i.TeamName).HasMaxLength(SlackInstallation.MaxTeamNameLength);
        builder.Property(i => i.BotUserId).HasMaxLength(SlackInstallation.MaxIdLength);
        builder.Property(i => i.ProtectedBotToken).HasMaxLength(SlackInstallation.MaxTokenLength);
        builder.Property(i => i.ChannelId).HasMaxLength(SlackInstallation.MaxIdLength);
        builder.Property(i => i.ChannelName).HasMaxLength(SlackInstallation.MaxChannelNameLength);
        // Event names as a native text[].
        builder.PrimitiveCollection(i => i.Events).HasColumnType("text[]").ElementType(e => e.HasConversion<string>());
        builder.Property(i => i.Problem).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // One workspace per organization.
        builder.HasIndex(i => i.OrganizationId).IsUnique();
        // The target of the account links' composite tenant key.
        builder.HasAlternateKey(i => new { i.OrganizationId, i.Id });
    }
}

internal sealed class SlackAccountLinkConfiguration : IEntityTypeConfiguration<SlackAccountLink>
{
    public void Configure(EntityTypeBuilder<SlackAccountLink> builder)
    {
        builder.Property(l => l.SlackUserId).HasMaxLength(SlackInstallation.MaxIdLength);
        builder.Property(l => l.SlackName).HasMaxLength(SlackAccountLink.MaxNameLength);
        builder.Property(l => l.Problem).HasConversion<string>().HasMaxLength(20);

        // Composite: a link always belongs to its own organization's workspace, and goes with it.
        builder.HasOne<SlackInstallation>().WithMany()
            .HasForeignKey(l => new { l.OrganizationId, l.InstallationId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // One account per member, and one member per account.
        builder.HasIndex(l => new { l.OrganizationId, l.UserId }).IsUnique();
        builder.HasIndex(l => new { l.OrganizationId, l.SlackUserId }).IsUnique();
    }
}

internal sealed class SlackMessageRecordConfiguration : IEntityTypeConfiguration<SlackMessageRecord>
{
    public void Configure(EntityTypeBuilder<SlackMessageRecord> builder)
    {
        builder.ToTable("slack_messages");
        builder.Property(m => m.IdempotencyKey).HasMaxLength(SlackMessageRecord.MaxKeyLength);
        builder.Property(m => m.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ErrorCode).HasMaxLength(SlackMessageRecord.MaxErrorLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The effect check: one record per message, so two handlers racing on one key cannot both commit.
        builder.HasIndex(m => new { m.OrganizationId, m.IdempotencyKey }).IsUnique();
        // The retention sweep.
        builder.HasIndex(m => new { m.OrganizationId, m.At });
    }
}

internal sealed class SlackSandboxMessageConfiguration : IEntityTypeConfiguration<SlackSandboxMessage>
{
    public void Configure(EntityTypeBuilder<SlackSandboxMessage> builder)
    {
        builder.Property(m => m.Conversation).HasMaxLength(SlackSandboxMessage.MaxConversationLength);
        builder.Property(m => m.Text).HasMaxLength(4000);

        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.OrganizationId, m.PostedAt });
    }
}
