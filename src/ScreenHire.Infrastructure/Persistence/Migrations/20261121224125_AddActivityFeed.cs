using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activity_seen_at",
                table: "member_preferences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_records_organization_id_subject_kind_occurred_at_id",
                table: "change_records",
                columns: new[] { "organization_id", "subject_kind", "occurred_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_application_events_organization_id_occurred_at_id",
                table: "application_events",
                columns: new[] { "organization_id", "occurred_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_change_records_organization_id_subject_kind_occurred_at_id",
                table: "change_records");

            migrationBuilder.DropIndex(
                name: "ix_application_events_organization_id_occurred_at_id",
                table: "application_events");

            migrationBuilder.DropColumn(
                name: "activity_seen_at",
                table: "member_preferences");
        }
    }
}
