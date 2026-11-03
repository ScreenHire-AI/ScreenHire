using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarConnections : Migration
    {
        // Every calendar table is tenant-owned: the same tenant_isolation policy as every other (docs/architecture/sign-in-and-tenancy.md).
        private const string EnableRowLevelSecurity = """
            ALTER TABLE calendar_connections ENABLE ROW LEVEL SECURITY;
            ALTER TABLE calendar_connections FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON calendar_connections
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE calendar_sandbox_busy ENABLE ROW LEVEL SECURITY;
            ALTER TABLE calendar_sandbox_busy FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON calendar_sandbox_busy
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE calendar_sandbox_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE calendar_sandbox_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON calendar_sandbox_events
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON calendar_sandbox_events;
            ALTER TABLE calendar_sandbox_events NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE calendar_sandbox_events DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON calendar_sandbox_busy;
            ALTER TABLE calendar_sandbox_busy NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE calendar_sandbox_busy DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON calendar_connections;
            ALTER TABLE calendar_connections NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE calendar_connections DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    protected_access_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    protected_refresh_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    access_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    problem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    problem_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_connections_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_sandbox_busy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_sandbox_busy", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_sandbox_busy_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_sandbox_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    meeting_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_sandbox_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_sandbox_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_connections_organization_id_user_id",
                table: "calendar_connections",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_sandbox_busy_organization_id_user_id_starts_at",
                table: "calendar_sandbox_busy",
                columns: new[] { "organization_id", "user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_sandbox_events_organization_id_user_id_provider_ev",
                table: "calendar_sandbox_events",
                columns: new[] { "organization_id", "user_id", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_sandbox_events_organization_id_user_id_starts_at",
                table: "calendar_sandbox_events",
                columns: new[] { "organization_id", "user_id", "starts_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "calendar_connections");

            migrationBuilder.DropTable(
                name: "calendar_sandbox_busy");

            migrationBuilder.DropTable(
                name: "calendar_sandbox_events");
        }
    }
}
