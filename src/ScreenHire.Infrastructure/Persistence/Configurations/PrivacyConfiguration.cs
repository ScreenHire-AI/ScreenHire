using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class DataRequestConfiguration : IEntityTypeConfiguration<DataRequest>
{
    public void Configure(EntityTypeBuilder<DataRequest> builder)
    {
        builder.ToTable("data_requests");
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DeclineReason).HasMaxLength(DataRequest.MaxDeclineReasonLength);
        builder.Property(r => r.ExportBlobName).HasMaxLength(DataRequest.MaxBlobNameLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // A single-column key, unlike the composite tenant keys of the application tables: SET NULL on a composite key would
        // null organization_id too. The row outlives an erased candidate with no link to them. No key to the resolving user:
        // like the team log, the record outlives a removed member.
        builder.HasOne<Candidate>().WithMany().HasForeignKey(r => r.CandidateId).OnDelete(DeleteBehavior.SetNull);

        // One open request of a kind per candidate, so a repeated click returns the existing request.
        builder.HasIndex(r => new { r.OrganizationId, r.CandidateId, r.Kind }).IsUnique().HasFilter("status = 'Open'");
        // The team's list: open first, soonest due first.
        builder.HasIndex(r => new { r.OrganizationId, r.Status, r.DueAt });
    }
}

internal sealed class RetentionSettingsConfiguration : IEntityTypeConfiguration<RetentionSettings>
{
    public void Configure(EntityTypeBuilder<RetentionSettings> builder)
    {
        builder.ToTable("retention_settings");
        builder.HasKey(s => s.OrganizationId);
        builder.HasOne<Organization>().WithOne()
            .HasForeignKey<RetentionSettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
