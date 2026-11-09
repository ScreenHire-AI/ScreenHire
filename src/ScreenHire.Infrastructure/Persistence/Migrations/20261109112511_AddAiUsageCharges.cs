using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageCharges : Migration
    {
        // Tenant isolation as every tenant table has it. Insert-only for the application role: a settled call is never edited, so
        // a charge reported once cannot be turned into one that is reported again. There is no job_scope policy: billing is
        // organization-level, like the ledger it settles.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE ai_usage_charges ENABLE ROW LEVEL SECURITY;
            ALTER TABLE ai_usage_charges FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON ai_usage_charges
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE ON ai_usage_charges FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON ai_usage_charges;
            ALTER TABLE ai_usage_charges DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_usage_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    micros = table.Column<long>(type: "bigint", nullable: false),
                    margin = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    provider = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);
            migrationBuilder.DropTable(
                name: "ai_usage_charges");
        }
    }
}
