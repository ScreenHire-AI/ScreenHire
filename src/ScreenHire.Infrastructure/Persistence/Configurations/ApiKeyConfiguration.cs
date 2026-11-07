using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.Property(k => k.Name).HasMaxLength(ApiKey.MaxNameLength);
        builder.Property(k => k.Prefix).HasMaxLength(20);
        builder.Property(k => k.SecretHash).HasMaxLength(64).IsFixedLength();
        // Scope names as a native text[].
        builder.PrimitiveCollection(k => k.Scopes).HasColumnType("text[]").ElementType(e => e.HasConversion<string>());

        builder.HasOne<Organization>().WithMany().HasForeignKey(k => k.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(k => k.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(k => k.OAuthClientId).HasColumnName("oauth_client_id");
        // A connection's client registration is purged only once no connection uses it.
        builder.HasOne<OAuthClient>().WithMany().HasForeignKey(k => k.OAuthClientId).OnDelete(DeleteBehavior.Restrict);
        // Authentication finds a key by its hash, with the organization from the key as the tenant, so the query filter
        // adds the organization to the lookup.
        builder.HasIndex(k => new { k.OrganizationId, k.SecretHash }).IsUnique();
        builder.HasIndex(k => new { k.OrganizationId, k.CreatedAt });
    }
}
