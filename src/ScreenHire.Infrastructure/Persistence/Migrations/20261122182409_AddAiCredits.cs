using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCredits : Migration
    {
        // ADR-0032: prepaid AI credits replace the metered charge and the monthly budget and cap.
        //
        // The nearest equivalent of each old limit, before the columns go. A dollar cap on ScreenHire's key becomes auto-reload's monthly
        // maximum, with auto-reload left off (it needs a card a purchase saves) and the default low-balance alert (5.00, from
        // packages/plans/credits.json). A budget only warned, and the low-balance alert replaces its warnings. A limit stored in tokens
        // has no dollar figure here (the prices are configuration, which SQL cannot read), and an own key's limit has no equivalent
        // because an own key never touches the balance: both are dropped, as the pull request says.
        //
        // verification_settings and notifications have FORCE ROW LEVEL SECURITY, which binds the owner running this migration too, so it is
        // lifted around the statements (transactional DDL: a failure rolls all of it back). The budget and cap notifications are no longer
        // true, so they go. The credit tables are filled before their own policies are forced.
        private const string CarryOverLimits = """
            ALTER TABLE verification_settings NO FORCE ROW LEVEL SECURITY;
            INSERT INTO credit_settings (id, organization_id, low_balance_alert, auto_reload_enabled, auto_reload_monthly_max, updated_at)
            SELECT gen_random_uuid(), organization_id, 5.00, false, monthly_cap, now()
            FROM verification_settings
            WHERE llm_source = 'Included' AND budget_unit = 'Money' AND monthly_cap IS NOT NULL;
            ALTER TABLE verification_settings FORCE ROW LEVEL SECURITY;
            ALTER TABLE notifications NO FORCE ROW LEVEL SECURITY;
            DELETE FROM notifications WHERE kind IN ('ai-usage-budget', 'ai-usage-cap');
            ALTER TABLE notifications FORCE ROW LEVEL SECURITY;
            """;

        // Tenant isolation as every tenant table has it, with no job_scope policy: credits are organization-level, like the AI usage
        // ledger. The credit ledger is append-only for the application role: no row is edited or removed, so a grant or a debit, once
        // written, stays (no retention purge touches it; the balance needs every row). A purchase changes status, so it keeps UPDATE.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE credit_entries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE credit_entries FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON credit_entries
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON credit_entries FROM screenhire_app;
            ALTER TABLE credit_purchases ENABLE ROW LEVEL SECURITY;
            ALTER TABLE credit_purchases FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON credit_purchases
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE credit_settings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE credit_settings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON credit_settings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    micros = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ai_call_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consistency_check_id = table.Column<Guid>(type: "uuid", nullable: true),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grant_entry_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_entries_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_purchases", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_purchases_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    low_balance_alert = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    auto_reload_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    auto_reload_below = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    auto_reload_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    auto_reload_monthly_max = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    payment_method_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_entries_organization_id_kind_at",
                table: "credit_entries",
                columns: new[] { "organization_id", "kind", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_entries_organization_id_source_key",
                table: "credit_entries",
                columns: new[] { "organization_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_purchases_one_pending_reload",
                table: "credit_purchases",
                column: "organization_id",
                unique: true,
                filter: "source = 'AutoReload' AND status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_credit_purchases_organization_id_created_at",
                table: "credit_purchases",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_settings_organization_id",
                table: "credit_settings",
                column: "organization_id",
                unique: true);

            migrationBuilder.Sql(CarryOverLimits);
            migrationBuilder.Sql(EnableRowLevelSecurity);

            // Metered AI use is gone: nothing reports to a meter, so its record goes (pre-launch: no organization was billed by it).
            migrationBuilder.DropTable(
                name: "ai_usage_charges");

            migrationBuilder.DropColumn(
                name: "budget_unit",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "monthly_budget",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "monthly_cap",
                table: "verification_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropTable(
                name: "credit_entries");

            migrationBuilder.DropTable(
                name: "credit_purchases");

            migrationBuilder.DropTable(
                name: "credit_settings");

            migrationBuilder.AddColumn<string>(
                name: "budget_unit",
                table: "verification_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Tokens");

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_budget",
                table: "verification_settings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_cap",
                table: "verification_settings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_usage_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    margin = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    micros = table.Column<long>(type: "bigint", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_usage_charges_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_charges_organization_id_state",
                table: "ai_usage_charges",
                columns: new[] { "organization_id", "state" });

            // As AddAiUsageCharges left it. The limits carried over are not put back: the columns come back empty.
            migrationBuilder.Sql("""
                ALTER TABLE ai_usage_charges ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ai_usage_charges FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON ai_usage_charges
                    USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                REVOKE UPDATE ON ai_usage_charges FROM screenhire_app;
                """);
        }
    }
}
