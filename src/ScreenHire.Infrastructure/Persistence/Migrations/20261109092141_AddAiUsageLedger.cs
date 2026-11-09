using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageLedger : Migration
    {
        // Tenant isolation as every tenant table has it (docs/architecture/sign-in-and-tenancy.md). The ledger is also append-only for the
        // application role: no UPDATE, so a call cannot be edited after it is recorded. The retention sweep deletes, so DELETE stays.
        // There is no job_scope policy on purpose: usage is organization-level, for Owners and Admins, and the application_id
        // column is a plain id that outlives an erased candidate.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE ai_usage_calls ENABLE ROW LEVEL SECURITY;
            ALTER TABLE ai_usage_calls FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON ai_usage_calls
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE ON ai_usage_calls FROM screenhire_app;

            ALTER TABLE ai_usage_alerts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE ai_usage_alerts FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON ai_usage_alerts
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE ON ai_usage_alerts FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON ai_usage_calls;
            ALTER TABLE ai_usage_calls DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON ai_usage_alerts;
            ALTER TABLE ai_usage_alerts DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<decimal>(
                name: "own_key_input_price",
                table: "verification_settings",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "own_key_output_price",
                table: "verification_settings",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            // The old token budget becomes the new budget, counted in tokens. The 80% alert's month claim is replaced by
            // ai_usage_alerts, so it is dropped with nothing to carry.
            migrationBuilder.Sql("UPDATE verification_settings SET monthly_budget = monthly_token_budget WHERE monthly_token_budget IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "budget_alert_month",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "monthly_token_budget",
                table: "verification_settings");

            migrationBuilder.CreateTable(
                name: "ai_usage_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_usage_alerts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_usage_calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    check_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    provider_request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    output_tokens = table.Column<long>(type: "bigint", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    cost_basis = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    error_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage_calls", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_usage_calls_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_alerts_organization_id_key",
                table: "ai_usage_alerts",
                columns: new[] { "organization_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_calls_organization_id_id",
                table: "ai_usage_calls",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_calls_organization_id_occurred_at",
                table: "ai_usage_calls",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "ai_usage_alerts");

            migrationBuilder.DropTable(
                name: "ai_usage_calls");

            migrationBuilder.AddColumn<long>(
                name: "monthly_token_budget",
                table: "verification_settings",
                type: "bigint",
                nullable: true);

            // A budget counted in tokens goes back as it was; one counted in dollars has no token form.
            migrationBuilder.Sql("UPDATE verification_settings SET monthly_token_budget = monthly_budget::bigint WHERE monthly_budget IS NOT NULL AND budget_unit = 'Tokens';");

            migrationBuilder.DropColumn(
                name: "budget_unit",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "monthly_budget",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "monthly_cap",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "own_key_input_price",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "own_key_output_price",
                table: "verification_settings");

            migrationBuilder.AddColumn<string>(
                name: "budget_alert_month",
                table: "verification_settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }
    }
}
