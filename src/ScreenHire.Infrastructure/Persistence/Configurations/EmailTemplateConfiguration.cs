using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.Property(t => t.BuiltInId).HasMaxLength(40);
        builder.Property(t => t.Name).HasMaxLength(EmailTemplate.MaxNameLength);
        builder.Property(t => t.Subject).HasMaxLength(EmailTemplate.MaxSubjectLength);
        builder.Property(t => t.Body).HasMaxLength(EmailTemplate.MaxBodyLength);
        builder.Ignore(t => t.Key);

        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // One wording of each built-in per organization; the organization's own templates list in creation order.
        builder.HasIndex(t => new { t.OrganizationId, t.BuiltInId }).IsUnique().HasFilter("built_in_id IS NOT NULL");
        builder.HasIndex(t => new { t.OrganizationId, t.CreatedAt });
    }
}
