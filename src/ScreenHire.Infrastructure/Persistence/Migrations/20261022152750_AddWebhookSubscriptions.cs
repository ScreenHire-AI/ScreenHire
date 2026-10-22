using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSubscriptions : Migration
    {
        // Tenant isolation for webhook endpoints and their delivery log (CLAUDE.md non-negotiable 6), as in
        // AddTelemetrySnapshots. screenhire_app keeps its default privileges: the API creates, changes and deletes endpoints
        // and adds replays; the worker adds deliveries, records attempts and counts failures.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE webhook_endpoints ENABLE ROW LEVEL SECURITY;
            ALTER TABLE webhook_endpoints FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON webhook_endpoints
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE webhook_deliveries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE webhook_deliveries FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON webhook_deliveries
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON webhook_deliveries;
            ALTER TABLE webhook_deliveries NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE webhook_deliveries DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON webhook_endpoints;
            ALTER TABLE webhook_endpoints NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE webhook_endpoints DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    events = table.Column<List<string>>(type: "text[]", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    secret_version = table.Column<int>(type: "integer", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    disabled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    secret_rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                    table.UniqueConstraint("ak_webhook_endpoints_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    replay_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_webhook_endpoints_organization_id_endpoi",
                        columns: x => new { x.organization_id, x.endpoint_id },
                        principalTable: "webhook_endpoints",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_organization_id_endpoint_id_created_at",
                table: "webhook_deliveries",
                columns: new[] { "organization_id", "endpoint_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_organization_id_endpoint_id_event_id",
                table: "webhook_deliveries",
                columns: new[] { "organization_id", "endpoint_id", "event_id" },
                unique: true,
                filter: "replay_of_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_organization_id_enabled",
                table: "webhook_endpoints",
                columns: new[] { "organization_id", "enabled" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "webhook_deliveries");

            migrationBuilder.DropTable(
                name: "webhook_endpoints");
        }
    }
}
