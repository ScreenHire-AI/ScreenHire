using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.ToTable("oauth_clients");
        builder.Property(c => c.Name).HasMaxLength(OAuthClient.MaxNameLength);
        builder.PrimitiveCollection(c => c.RedirectUris).HasColumnType("text[]");
        // The purge finds registrations nobody approved.
        builder.HasIndex(c => c.CreatedAt);
    }
}

internal sealed class OAuthCodeConfiguration : IEntityTypeConfiguration<OAuthCode>
{
    public void Configure(EntityTypeBuilder<OAuthCode> builder)
    {
        builder.ToTable("oauth_codes");
        builder.Property(c => c.CodeHash).HasMaxLength(64).IsFixedLength();
        builder.Property(c => c.RedirectUri).HasMaxLength(2048);
        builder.Property(c => c.CodeChallenge).HasMaxLength(128);

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApiKey>().WithMany().HasForeignKey(c => c.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
        // The token endpoint finds a code by its hash, with the organization from the code as the tenant.
        builder.HasIndex(c => new { c.OrganizationId, c.CodeHash }).IsUnique();
        builder.HasIndex(c => c.ApiKeyId);
        builder.HasIndex(c => c.ExpiresAt);
    }
}

internal sealed class OAuthTokenConfiguration : IEntityTypeConfiguration<OAuthToken>
{
    public void Configure(EntityTypeBuilder<OAuthToken> builder)
    {
        builder.ToTable("oauth_tokens");
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength();

        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApiKey>().WithMany().HasForeignKey(t => t.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
        // Authentication finds a token by its hash, with the organization from the token as the tenant.
        builder.HasIndex(t => new { t.OrganizationId, t.TokenHash }).IsUnique();
        builder.HasIndex(t => t.ApiKeyId);
        builder.HasIndex(t => t.ExpiresAt);
    }
}
