using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrityEngine : Migration
    {
        // Tenant isolation for the integrity tables (CLAUDE.md non-negotiable 6), as in AddSignInAndTenantIsolation;
        // screenhire_app's grants come from that migration's default privileges. Signals, analyzer runs, assessments and
        // review actions are append-only records for audit (ADR-0004), so the application role may not change or
        // delete them. Submission observations hold raw network data that the retention job deletes, so only UPDATE is
        // revoked there. The Signal Network table is cross-tenant and behind a feature flag that is off (ADR-0005): row-level
        // security with no policy and no grants, so the application role cannot read or write it at all.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE integrity_analyzer_runs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE integrity_analyzer_runs FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON integrity_analyzer_runs
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON integrity_analyzer_runs FROM screenhire_app;

            ALTER TABLE integrity_signals ENABLE ROW LEVEL SECURITY;
            ALTER TABLE integrity_signals FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON integrity_signals
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON integrity_signals FROM screenhire_app;

            ALTER TABLE integrity_assessments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE integrity_assessments FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON integrity_assessments
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON integrity_assessments FROM screenhire_app;

            ALTER TABLE integrity_review_actions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE integrity_review_actions FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON integrity_review_actions
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON integrity_review_actions FROM screenhire_app;

            ALTER TABLE submission_observations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE submission_observations FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON submission_observations
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE ON submission_observations FROM screenhire_app;

            ALTER TABLE signal_network_entries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE signal_network_entries FORCE ROW LEVEL SECURITY;
            REVOKE ALL ON signal_network_entries FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON integrity_analyzer_runs;
            ALTER TABLE integrity_analyzer_runs NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE integrity_analyzer_runs DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON integrity_signals;
            ALTER TABLE integrity_signals NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE integrity_signals DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON integrity_assessments;
            ALTER TABLE integrity_assessments NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE integrity_assessments DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON integrity_review_actions;
            ALTER TABLE integrity_review_actions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE integrity_review_actions DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON submission_observations;
            ALTER TABLE submission_observations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE submission_observations DISABLE ROW LEVEL SECURITY;
            ALTER TABLE signal_network_entries NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE signal_network_entries DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "signal_network_opt_in",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "integrity_analyzer_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyzer = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    analyzer_version = table.Column<int>(type: "integer", nullable: false),
                    signal_count = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrity_analyzer_runs", x => x.id);
                    table.UniqueConstraint("ak_integrity_analyzer_runs_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_integrity_analyzer_runs_applications_organization_id_applic",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integrity_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ruleset_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    signal_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    counted_signal_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    applied_rules = table.Column<List<string>>(type: "text[]", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    next_step = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ai_use_policy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ai_use_declaration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ai_policy_context = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrity_assessments", x => x.id);
                    table.UniqueConstraint("ak_integrity_assessments_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_integrity_assessments_applications_organization_id_applicat",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "signal_network_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    identifier_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contributor_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signal_network_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submission_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    device_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    form_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    automation_markers = table.Column<List<string>>(type: "text[]", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_observations", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_observations_applications_organization_id_applic",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integrity_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analyzer_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assessment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weight = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_version = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accessibility_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    context_only = table.Column<bool>(type: "boolean", nullable: false),
                    explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    counter_evidence = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrity_signals", x => x.id);
                    table.ForeignKey(
                        name: "fk_integrity_signals_applications_organization_id_application_",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_integrity_signals_integrity_analyzer_runs_organization_id_a",
                        columns: x => new { x.organization_id, x.analyzer_run_id },
                        principalTable: "integrity_analyzer_runs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integrity_review_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dismiss_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integrity_review_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_integrity_review_actions_applications_organization_id_appli",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_integrity_review_actions_integrity_assessments_organization",
                        columns: x => new { x.organization_id, x.assessment_id },
                        principalTable: "integrity_assessments",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integrity_analyzer_runs_organization_id_application_id_anal",
                table: "integrity_analyzer_runs",
                columns: new[] { "organization_id", "application_id", "analyzer", "analyzer_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integrity_assessments_organization_id_application_id_kind_a",
                table: "integrity_assessments",
                columns: new[] { "organization_id", "application_id", "kind", "assessed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_integrity_assessments_organization_id_application_id_kind_f",
                table: "integrity_assessments",
                columns: new[] { "organization_id", "application_id", "kind", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integrity_review_actions_organization_id_application_id_occ",
                table: "integrity_review_actions",
                columns: new[] { "organization_id", "application_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_integrity_review_actions_organization_id_assessment_id",
                table: "integrity_review_actions",
                columns: new[] { "organization_id", "assessment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_integrity_signals_organization_id_analyzer_run_id",
                table: "integrity_signals",
                columns: new[] { "organization_id", "analyzer_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_integrity_signals_organization_id_application_id",
                table: "integrity_signals",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_signal_network_entries_expires_at",
                table: "signal_network_entries",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_signal_network_entries_kind_identifier_hash_seen_at",
                table: "signal_network_entries",
                columns: new[] { "kind", "identifier_hash", "seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_observations_organization_id_application_id",
                table: "submission_observations",
                columns: new[] { "organization_id", "application_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_observations_organization_id_device_hash",
                table: "submission_observations",
                columns: new[] { "organization_id", "device_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_observations_organization_id_expires_at",
                table: "submission_observations",
                columns: new[] { "organization_id", "expires_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "integrity_review_actions");

            migrationBuilder.DropTable(
                name: "integrity_signals");

            migrationBuilder.DropTable(
                name: "signal_network_entries");

            migrationBuilder.DropTable(
                name: "submission_observations");

            migrationBuilder.DropTable(
                name: "integrity_assessments");

            migrationBuilder.DropTable(
                name: "integrity_analyzer_runs");

            migrationBuilder.DropColumn(
                name: "signal_network_opt_in",
                table: "organizations");
        }
    }
}
