using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>hosted_fonts</c>: one row per Google Fonts family ScreenHire has downloaded (ADR-0031). Not tenant-owned and with
/// no row-level security: font files are public and the same for every organization, and nothing in the row says which
/// organization picked the family. The id is the key, so two first saves of one family at once insert it once.
/// </summary>
internal sealed class HostedFontConfiguration : IEntityTypeConfiguration<HostedFont>
{
    public void Configure(EntityTypeBuilder<HostedFont> builder)
    {
        builder.Property(f => f.Id).HasMaxLength(HeadingFont.MaxIdLength).ValueGeneratedNever();
        builder.Property(f => f.Family).HasMaxLength(HeadingFont.MaxFamilyLength);
        builder.Property(f => f.Category).HasMaxLength(20);
        builder.Property(f => f.License).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Version).HasMaxLength(40);
        builder.Property(f => f.Files).HasJsonColumn();
    }
}
