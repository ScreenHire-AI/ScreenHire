using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentForensics : Migration
    {
        // Tenant isolation for the content forensics tables (CLAUDE.md non-negotiable 6), as in AddIntegrityEngine;
        // screenhire_app's grants come from AddSignInAndTenantIsolation's default privileges. Extracted documents and
        // answer fingerprints are written once, so the application role may not change or delete them. A consistency
        // check is claimed and then completed, and an organization's settings change, so those keep UPDATE; neither is
        // deleted by the application.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE application_documents ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_documents FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_documents
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON application_documents FROM screenhire_app;

            ALTER TABLE answer_fingerprints ENABLE ROW LEVEL SECURITY;
            ALTER TABLE answer_fingerprints FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON answer_fingerprints
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON answer_fingerprints FROM screenhire_app;

            ALTER TABLE consistency_checks ENABLE ROW LEVEL SECURITY;
            ALTER TABLE consistency_checks FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON consistency_checks
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE DELETE ON consistency_checks FROM screenhire_app;

            ALTER TABLE integrity_settings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE integrity_settings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON integrity_settings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE DELETE ON integrity_settings FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON application_documents;
            ALTER TABLE application_documents NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE application_documents DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON answer_fingerprints;
            ALTER TABLE answer_fingerprints NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE answer_fingerprints DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON consistency_checks;
            ALTER TABLE consistency_checks NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE consistency_checks DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON integrity_settings;
            ALTER TABLE integrity_settings NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE integrity_settings DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "informational",
                table: "integrity_signals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "llm",
                table: "integrity_signals",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_application_files_organization_id_id",
                table: "application_files",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateTable(
                name: "answer_fingerprints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sim_hash = table.Column<long>(type: "bigint", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_answer_fingerprints", x => x.id);
                    table.ForeignKey(
                        name: "fk_answer_fingerprints_applications_organization_id_applicatio",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extractor_version = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(60000)", maxLength: 60000, nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    hidden_text = table.Column<string>(type: "jsonb", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_documents_application_files_organization_id_fil",
                        columns: x => new { x.organization_id, x.file_id },
                        principalTable: "application_files",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_application_documents_applications_organization_id_applicat",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consistency_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    extraction_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consistency_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contradictions = table.Column<string>(type: "jsonb", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consistency_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_consistency_checks_applications_organization_id_application",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integrity_settings",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    llm_consistency_check_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    monthly_token_budget = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrity_settings", x => x.organization_id);
                    table.ForeignKey(
                        name: "fk_integrity_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_answer_fingerprints_organization_id_application_id_question",
                table: "answer_fingerprints",
                columns: new[] { "organization_id", "application_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_answer_fingerprints_organization_id_created_at",
                table: "answer_fingerprints",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_application_documents_organization_id_application_id",
                table: "application_documents",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_application_documents_organization_id_file_id_extractor_ver",
                table: "application_documents",
                columns: new[] { "organization_id", "file_id", "extractor_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consistency_checks_organization_id_application_id_prompt_ve",
                table: "consistency_checks",
                columns: new[] { "organization_id", "application_id", "prompt_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consistency_checks_organization_id_started_at",
                table: "consistency_checks",
                columns: new[] { "organization_id", "started_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "answer_fingerprints");

            migrationBuilder.DropTable(
                name: "application_documents");

            migrationBuilder.DropTable(
                name: "consistency_checks");

            migrationBuilder.DropTable(
                name: "integrity_settings");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_application_files_organization_id_id",
                table: "application_files");

            migrationBuilder.DropColumn(
                name: "informational",
                table: "integrity_signals");

            migrationBuilder.DropColumn(
                name: "llm",
                table: "integrity_signals");
        }
    }
}
