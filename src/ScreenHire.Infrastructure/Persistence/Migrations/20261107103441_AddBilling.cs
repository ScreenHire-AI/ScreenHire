using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBilling : Migration
    {
        // Both billing tables are tenant-owned: the same tenant_isolation policy as every other (docs/architecture/sign-in-and-tenancy.md).
        private const string EnableRowLevelSecurity = """
            ALTER TABLE billing_accounts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE billing_accounts FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON billing_accounts
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE billing_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE billing_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON billing_events
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON billing_events;
            ALTER TABLE billing_events NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE billing_events DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON billing_accounts;
            ALTER TABLE billing_accounts NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE billing_accounts DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancels_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    last_event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_billing_accounts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_billing_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_accounts_organization_id",
                table: "billing_accounts",
                column: "organization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_events_organization_id_event_id",
                table: "billing_events",
                columns: new[] { "organization_id", "event_id" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "billing_accounts");

            migrationBuilder.DropTable(
                name: "billing_events");
        }
    }
}
