using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "completed_by_user_id",
                table: "application_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_tasks_organization_id_assignee_user_id_complete",
                table: "application_tasks",
                columns: new[] { "organization_id", "assignee_user_id", "completed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_application_tasks_organization_id_assignee_user_id_complete",
                table: "application_tasks");

            migrationBuilder.DropColumn(
                name: "completed_by_user_id",
                table: "application_tasks");
        }
    }
}
