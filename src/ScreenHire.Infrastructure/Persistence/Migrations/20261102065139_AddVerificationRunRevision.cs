using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationRunRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs");

            migrationBuilder.AddColumn<int>(
                name: "source_revision",
                table: "verification_signals",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "verification_review_actions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "revision",
                table: "verification_analyzer_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs",
                columns: new[] { "organization_id", "application_id", "analyzer", "analyzer_version", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs");

            migrationBuilder.DropColumn(
                name: "source_revision",
                table: "verification_signals");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "verification_analyzer_runs");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "verification_review_actions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs",
                columns: new[] { "organization_id", "application_id", "analyzer", "analyzer_version" },
                unique: true);
        }
    }
}
