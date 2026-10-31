using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.TalentPool;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class TalentPoolEntryConfiguration : IEntityTypeConfiguration<TalentPoolEntry>
{
    public void Configure(EntityTypeBuilder<TalentPoolEntry> builder)
    {
        builder.ToTable("talent_pool_entries");

        // The entry goes with its application or its candidate, so a future erasure of either takes it out of the pool.
        builder.HasOne<JobApplication>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Candidate>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.CandidateId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // One entry per candidate: keeping them from another application replaces it.
        builder.HasIndex(e => new { e.OrganizationId, e.CandidateId }).IsUnique();
        builder.HasIndex(e => new { e.OrganizationId, e.ApplicationId });
    }
}

internal sealed class TalentPoolSettingsConfiguration : IEntityTypeConfiguration<TalentPoolSettings>
{
    public void Configure(EntityTypeBuilder<TalentPoolSettings> builder)
    {
        builder.ToTable("talent_pool_settings");
        builder.HasKey(s => s.OrganizationId);
        builder.HasOne<Organization>().WithOne()
            .HasForeignKey<TalentPoolSettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
