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

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // Target of the applications' composite tenant foreign key.
        builder.HasAlternateKey(c => new { c.OrganizationId, c.Id });
        builder.HasIndex(c => new { c.OrganizationId, c.NormalizedEmail }).IsUnique();
    }
}
