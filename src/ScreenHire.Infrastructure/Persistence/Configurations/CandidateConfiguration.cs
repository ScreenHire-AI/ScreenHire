using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.Property(c => c.FirstName).HasMaxLength(Candidate.MaxFirstNameLength);
        builder.Property(c => c.LastName).HasMaxLength(Candidate.MaxNamePartLength);
        builder.Property(c => c.FullName).HasMaxLength(Candidate.MaxFullNameLength);
        builder.Property(c => c.Email).HasMaxLength(320);
        builder.Property(c => c.NormalizedEmail).HasMaxLength(320);
        builder.Property(c => c.Phone).HasMaxLength(50);
        // Existing candidates have no delivery problem; the column default keeps every old row valid.
        builder.Property(c => c.EmailIssue).HasConversion<string>().HasMaxLength(20).HasDefaultValue(CandidateEmailIssue.None);
        builder.Property(c => c.EmailIssueReason).HasMaxLength(Candidate.MaxEmailIssueReasonLength);

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // Target of the applications' composite tenant foreign key.
        builder.HasAlternateKey(c => new { c.OrganizationId, c.Id });
        builder.HasIndex(c => new { c.OrganizationId, c.NormalizedEmail }).IsUnique();
        // Trigram indexes for the search box's ILIKE '%text%' (GET /v1/search, the applications list and the MCP tools).
        // A term shorter than three characters has no trigram, so it still scans. Under row-level security the planner
        // uses an index for a user's condition only when its function is leakproof, which ILIKE is not, so the app role
        // does not use these today; see docs/architecture/sign-in-and-tenancy.md.
        builder.HasIndex(c => c.FullName).HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(c => c.Email).HasMethod("gin").HasOperators("gin_trgm_ops");
    }
}
