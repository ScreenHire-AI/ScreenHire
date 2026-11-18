using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Views;
using ScreenHire.Infrastructure.Identity;

namespace ScreenHire.Infrastructure.Persistence.Configurations;

internal sealed class SavedViewMemberConfiguration : IEntityTypeConfiguration<SavedViewMember>
{
    public void Configure(EntityTypeBuilder<SavedViewMember> builder)
    {
        builder.ToTable("saved_view_members");
        builder.HasKey(m => new { m.ViewId, m.UserId });

        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // The row goes with the view (the Undo purge is a bulk delete) and with the person.
        builder.HasOne<SavedFilterView>().WithMany().HasForeignKey(m => m.ViewId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);

        // The sidebar reads one person's added views in order.
        builder.HasIndex(m => new { m.OrganizationId, m.UserId, m.Position });
        builder.HasIndex(m => m.UserId);
    }
}
