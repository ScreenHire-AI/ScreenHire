using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class MagicLinkTokenConfiguration : IEntityTypeConfiguration<MagicLinkToken>
{
    public void Configure(EntityTypeBuilder<MagicLinkToken> builder)
    {
        builder.Property(t => t.Email).HasMaxLength(256);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        // The per-address rate limit counts recent links.
        builder.HasIndex(t => new { t.Email, t.CreatedAt });
    }
}
