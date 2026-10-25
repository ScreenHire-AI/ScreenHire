using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class ScorecardTemplateConfiguration : IEntityTypeConfiguration<ScorecardTemplate>
{
    public void Configure(EntityTypeBuilder<ScorecardTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(ScorecardTemplate.MaxNameLength);
        builder.Property(t => t.Questions).HasJsonColumn();
        builder.Property(t => t.CopiedFrom).HasMaxLength(40);
        builder.Ignore(t => t.Key);

        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The library lists an organization's own scorecards by name.
        builder.HasIndex(t => new { t.OrganizationId, t.Name });
    }
}
