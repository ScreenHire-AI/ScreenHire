using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class HiringTemplateConfiguration : IEntityTypeConfiguration<HiringTemplate>
{
    public void Configure(EntityTypeBuilder<HiringTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(120);
        builder.Property(t => t.Description).HasMaxLength(1000);
        // Member ids as a native uuid[] (ADR-0015); always empty on a system template.
        builder.PrimitiveCollection(t => t.DefaultHiringTeam).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
        builder.Ignore(t => t.Draft);

        // Null for a system template.
        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.OrganizationId, t.Name });

        builder.HasMany(t => t.Versions).WithOne().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class HiringTemplateVersionConfiguration : IEntityTypeConfiguration<HiringTemplateVersion>
{
    public void Configure(EntityTypeBuilder<HiringTemplateVersion> builder)
    {
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Definition).HasJsonColumn();

        builder.HasOne<Organization>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => v.OrganizationId);
        builder.HasIndex(v => new { v.TemplateId, v.Version }).IsUnique();
        // At most one draft per template, even under concurrent requests.
        builder.HasIndex(v => v.TemplateId, "ix_hiring_template_versions_one_draft").IsUnique().HasFilter("status = 'Draft'");
    }
}
