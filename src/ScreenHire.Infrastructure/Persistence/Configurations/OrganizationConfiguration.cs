using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(o => o.Name).HasMaxLength(200);
        builder.Property(o => o.Slug).HasMaxLength(63);
        builder.Property(o => o.HomeRegion).HasMaxLength(16);
        builder.Property(o => o.BrandColor).HasMaxLength(7);
        builder.Property(o => o.SecondColor).HasMaxLength(7);
        // The column kept its name from when it held one of six enum names; it holds a font id since ADR-0031.
        builder.Property(o => o.HeadingFontId).HasColumnName("heading_font").HasMaxLength(HeadingFont.MaxIdLength);
        builder.Property(o => o.HeadingFontFamily).HasMaxLength(HeadingFont.MaxFamilyLength);
        builder.Ignore(o => o.HeadingFont);
        // Existing organizations let no other site frame their careers pages.
        builder.Property(o => o.EmbedOrigins).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        // Existing organizations have skipped no setup step and hidden nothing.
        builder.PrimitiveCollection(o => o.SkippedSetupSteps).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]")
            .ElementType(e => e.HasConversion<string>());
        builder.HasIndex(o => o.Slug).IsUnique();
        builder.Ignore(o => o.MayUsePartnerBoards);
    }
}
