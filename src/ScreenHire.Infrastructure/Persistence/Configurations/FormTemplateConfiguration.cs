using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class FormTemplateConfiguration : IEntityTypeConfiguration<FormTemplate>
{
    public void Configure(EntityTypeBuilder<FormTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(DefinitionValidator.MaxFormNameLength);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Ignore(t => t.Draft);
        builder.Ignore(t => t.LatestPublished);

        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The library lists an organization's forms by name.
        builder.HasIndex(t => new { t.OrganizationId, t.Name });

        // A composite key with the organization, so a version always belongs to its form's organization.
        builder.HasMany(t => t.Versions).WithOne()
            .HasForeignKey(v => new { v.OrganizationId, v.FormTemplateId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class FormTemplateVersionConfiguration : IEntityTypeConfiguration<FormTemplateVersion>
{
    public void Configure(EntityTypeBuilder<FormTemplateVersion> builder)
    {
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Content).HasJsonColumn();

        builder.HasOne<Organization>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => v.OrganizationId);
        builder.HasIndex(v => new { v.FormTemplateId, v.Version }).IsUnique();
        // At most one draft per form, even under concurrent requests.
        builder.HasIndex(v => v.FormTemplateId, "ix_form_template_versions_one_draft").IsUnique().HasFilter("status = 'Draft'");
    }
}
