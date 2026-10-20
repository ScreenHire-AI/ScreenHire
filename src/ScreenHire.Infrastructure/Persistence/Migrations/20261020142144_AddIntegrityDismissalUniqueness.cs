using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrityDismissalUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_integrity_review_actions_one_dismissal",
                table: "integrity_review_actions",
                columns: new[] { "organization_id", "assessment_id" },
                unique: true,
                filter: "type = 'Dismissed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_integrity_review_actions_one_dismissal",
                table: "integrity_review_actions");
        }
    }
}
