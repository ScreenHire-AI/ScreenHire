using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataRequests : Migration
    {
        // Both tables are tenant-owned: the same tenant_isolation policy as every other (docs/architecture/sign-in-and-tenancy.md).
        // The application role keeps its default privileges on them: a request is updated as it is answered, and the erasure
        // route writes to it, so nothing here is append-only.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE data_requests ENABLE ROW LEVEL SECURITY;
            ALTER TABLE data_requests FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON data_requests
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE retention_settings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE retention_settings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON retention_settings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // The model before this migration cannot read these kinds, so a rollback removes the entries. The table is forced, so
        // with no tenant set the delete matches nothing unless the force is lifted around it.
        private const string RemoveDataRequestLogEntries = """
            ALTER TABLE membership_events NO FORCE ROW LEVEL SECURITY;
            DELETE FROM membership_events
                WHERE kind IN ('DataRequestReceived', 'DataRequestCopySent', 'DataRequestDeclined', 'RetentionApplied');
            ALTER TABLE membership_events FORCE ROW LEVEL SECURITY;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON retention_settings;
            ALTER TABLE retention_settings NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE retention_settings DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON data_requests;
            ALTER TABLE data_requests NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE data_requests DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "data_request_id",
                table: "membership_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "item_count",
                table: "membership_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "data_request_link_sent_at",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decline_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    export_blob_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    export_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    copy_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_requests_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_data_requests_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retention_settings",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ended_applications_months = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_settings", x => x.organization_id);
                    table.ForeignKey(
                        name: "fk_retention_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_requests_candidate_id",
                table: "data_requests",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_requests_organization_id_candidate_id_kind",
                table: "data_requests",
                columns: new[] { "organization_id", "candidate_id", "kind" },
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_data_requests_organization_id_status_due_at",
                table: "data_requests",
                columns: new[] { "organization_id", "status", "due_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);
            migrationBuilder.Sql(RemoveDataRequestLogEntries);

            migrationBuilder.DropTable(
                name: "data_requests");

            migrationBuilder.DropTable(
                name: "retention_settings");

            migrationBuilder.DropColumn(
                name: "data_request_id",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "item_count",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "data_request_link_sent_at",
                table: "candidates");
        }
    }
}
