using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareersApplications : Migration
    {
        // Tenant isolation for application_files (CLAUDE.md non-negotiable 6): the jobs block of
        // AddSignInAndTenantIsolation. screenhire_app's grants come from that migration's default privileges. Files are
        // never deleted by the application role; they go with their application.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE application_files ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_files FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_files
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE DELETE ON application_files FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON application_files;
            ALTER TABLE application_files NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE application_files DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brand_color",
                table: "organizations",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "answers",
                table: "applications",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "bot_check",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotChecked");

            migrationBuilder.AddColumn<bool>(
                name: "honeypot_filled",
                table: "applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "privacy_notice_version",
                table: "applications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "applications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Recruiter");

            migrationBuilder.CreateTable(
                name: "application_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    upload_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blob_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_files_applications_organization_id_application_",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_files_organization_id_application_id_question_id",
                table: "application_files",
                columns: new[] { "organization_id", "application_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_files_organization_id_sha256",
                table: "application_files",
                columns: new[] { "organization_id", "sha256" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "application_files");

            migrationBuilder.DropColumn(
                name: "brand_color",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "answers",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "bot_check",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "honeypot_filled",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "privacy_notice_version",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "source",
                table: "applications");
        }
    }
}
