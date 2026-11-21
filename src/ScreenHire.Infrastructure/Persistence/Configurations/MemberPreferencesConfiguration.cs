using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Notifications;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class MemberPreferencesConfiguration : IEntityTypeConfiguration<MemberPreferences>
{
    public void Configure(EntityTypeBuilder<MemberPreferences> builder)
    {
        builder.ToTable("member_preferences");
        builder.Property(p => p.TimeZone).HasMaxLength(MemberPreferences.MaxTimeZoneLength);
        builder.Property(p => p.Language).HasMaxLength(MemberPreferences.MaxLanguageLength);
        builder.Property(p => p.DateFormat).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Notices).HasJsonColumn();

        builder.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // Goes with the membership: someone who leaves and rejoins starts from the defaults.
        builder.HasOne<OrganizationMember>().WithMany().HasForeignKey(p => p.MemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        // One row per membership; the digest finds a person by user id.
        builder.HasIndex(p => new { p.OrganizationId, p.MemberId }).IsUnique();
        builder.HasIndex(p => new { p.OrganizationId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.MemberId);
        builder.HasIndex(p => p.UserId);
    }
}

internal sealed class DigestItemConfiguration : IEntityTypeConfiguration<DigestItem>
{
    public void Configure(EntityTypeBuilder<DigestItem> builder)
    {
        builder.ToTable("digest_items");
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.SourceId).HasMaxLength(DigestItem.MaxSourceIdLength);
        builder.Property(i => i.Detail).HasMaxLength(DigestItem.MaxDetailLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        // One item per thing per person, so a redelivered notice queues it once.
        builder.HasIndex(i => new { i.OrganizationId, i.UserId, i.Kind, i.SourceId }).IsUnique();
        // The digest reads one person's unsent items; the purge reads sent ones by age.
        builder.HasIndex(i => new { i.OrganizationId, i.UserId, i.SentAt });
        builder.HasIndex(i => i.UserId);
    }
}

internal sealed class UserPhotoConfiguration : IEntityTypeConfiguration<UserPhoto>
{
    public void Configure(EntityTypeBuilder<UserPhoto> builder)
    {
        builder.ToTable("user_photos");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.ContentType).HasMaxLength(UserPhoto.MaxContentTypeLength);
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserPhoto>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
