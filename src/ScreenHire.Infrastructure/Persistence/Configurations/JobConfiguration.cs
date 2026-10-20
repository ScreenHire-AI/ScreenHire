using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.Property(j => j.Title).HasMaxLength(200);
        builder.Property(j => j.Slug).HasMaxLength(120);
        // Sanitized HTML. The API caps the input; sanitizing can lengthen it a little (entity encoding), so no column cap.
        builder.Property(j => j.Description).HasColumnType("text");
        builder.Property(j => j.Department).HasMaxLength(100);
        builder.Property(j => j.Locations).HasJsonColumn();
        // Country codes as a native text[].
        builder.Property(j => j.ApplicantLocationRequirements);
        builder.Property(j => j.SalaryCurrency).HasMaxLength(3);
        builder.Property(j => j.SalaryMin).HasPrecision(12, 2);
        builder.Property(j => j.SalaryMax).HasPrecision(12, 2);
        builder.Ignore(j => j.Salary);
        builder.Property(j => j.Workflow).HasJsonColumn();

        // Enums as text: readable in SQL and stable when members are reordered.
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.WorkplaceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.EmploymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.SalaryPeriod).HasConversion<string>().HasMaxLength(20);

        // Optimistic concurrency on Postgres' xmin system column (no real column): a publish that loaded the job before
        // someone closed it fails instead of writing Status = Open back over the close.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Organization>().WithMany().HasForeignKey(j => j.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The target of the composite tenant foreign keys on job_versions and applications: a child row can only point at
        // a job of its own organization, whatever id a request carries (foreign key checks bypass row-level security).
        builder.HasAlternateKey(j => new { j.OrganizationId, j.Id });
        builder.HasIndex(j => new { j.OrganizationId, j.Slug }).IsUnique();
        builder.HasIndex(j => new { j.OrganizationId, j.Status });

        // The source template may be a system template (no organization), so these keys are plain; the API loads the
        // version through the tenant filter before it links one.
        builder.HasOne<HiringTemplate>().WithMany().HasForeignKey(j => j.SourceTemplateId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<HiringTemplateVersion>().WithMany().HasForeignKey(j => j.SourceTemplateVersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(j => new { j.OrganizationId, j.SourceTemplateId });
    }
}
