using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Views;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class SavedFilterViewConfiguration : IEntityTypeConfiguration<SavedFilterView>
{
    public void Configure(EntityTypeBuilder<SavedFilterView> builder)
    {
        builder.ToTable("saved_filter_views", t =>
        {
            t.HasCheckConstraint("ck_saved_filter_views_scope", "scope IN ('Candidates', 'Job')");
            t.HasCheckConstraint("ck_saved_filter_views_job", "(scope = 'Job') = (job_id IS NOT NULL)");
        });
        builder.Property(v => v.Name).HasMaxLength(SavedFilterView.MaxNameLength);
        builder.Property(v => v.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Definition).HasJsonColumn();

        builder.HasOne<Organization>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // A person's views go with them; a job's views go with the job. Composite, so a view can only name a job of its own
        // organization.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(v => v.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.JobId })
            .HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // The list a member opens reads their own views and the shared ones. Names are unique per person and table without
        // regard to case: ux_saved_filter_views_name is an expression index, so it lives in the AddSavedViews migration.
        builder.HasIndex(v => new { v.OrganizationId, v.IsShared, v.OwnerUserId });
    }
}
