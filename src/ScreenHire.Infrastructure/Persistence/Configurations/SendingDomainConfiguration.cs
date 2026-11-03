using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class SendingDomainConfiguration : IEntityTypeConfiguration<SendingDomain>
{
    public void Configure(EntityTypeBuilder<SendingDomain> builder)
    {
        builder.Property(d => d.Domain).HasMaxLength(SendingDomain.MaxLength);
        builder.Property(d => d.ProviderDomainId).HasMaxLength(100);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Records).HasJsonColumn();
        builder.Property(d => d.ProviderMessage).HasMaxLength(300);

        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // One sending domain per organization. A domain is also owned by one organization: two companies cannot send as the same name.
        builder.HasIndex(d => d.OrganizationId).IsUnique();
        builder.HasIndex(d => d.Domain).IsUnique();
    }
}
