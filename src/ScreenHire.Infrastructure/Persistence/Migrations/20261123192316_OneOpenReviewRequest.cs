using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneOpenReviewRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_application_tasks_open_review_request",
                table: "application_tasks",
                columns: new[] { "organization_id", "application_id" },
                unique: true,
                filter: "kind = 'ReviewRequest' AND completed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_application_tasks_open_review_request",
                table: "application_tasks");
        }
    }
}
