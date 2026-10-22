using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        // A preference, not a membership: leaving the organization does not clear it, so the API checks membership
        // before using it. The foreign key only stops it pointing at a deleted organization.
        builder.HasOne<Organization>().WithMany().HasForeignKey(u => u.ActiveOrganizationId).OnDelete(DeleteBehavior.SetNull);

        // Identity's own indexes, renamed to the snake_case convention. The email index is unique: sign-in links an
        // identity provider to a user by verified email, so two users must never share one.
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email").IsUnique();
        builder.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
    }
}
