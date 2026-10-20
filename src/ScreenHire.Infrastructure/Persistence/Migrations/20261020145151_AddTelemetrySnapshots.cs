using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetrySnapshots : Migration
    {
        // Tenant isolation for form telemetry (CLAUDE.md non-negotiable 6), as in AddIntegrityEngine. screenhire_app keeps
        // its default privileges: ingest upserts snapshots, submission binds (updates) or deletes them, and the retention
        // job deletes expired ones.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE telemetry_snapshots ENABLE ROW LEVEL SECURITY;
            ALTER TABLE telemetry_snapshots FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON telemetry_snapshots
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON telemetry_snapshots;
            ALTER TABLE telemetry_snapshots NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE telemetry_snapshots DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telemetry_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_view_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    page = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telemetry_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_telemetry_snapshots_applications_organization_id_applicatio",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_telemetry_snapshots_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_snapshots_organization_id_application_id",
                table: "telemetry_snapshots",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_snapshots_organization_id_expires_at",
                table: "telemetry_snapshots",
                columns: new[] { "organization_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_snapshots_organization_id_job_id",
                table: "telemetry_snapshots",
                columns: new[] { "organization_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_snapshots_organization_id_session_id_page_view_id",
                table: "telemetry_snapshots",
                columns: new[] { "organization_id", "session_id", "page_view_id" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "telemetry_snapshots");
        }
    }
}
