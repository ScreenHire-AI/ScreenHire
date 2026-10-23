using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplates : Migration
    {
        // Tenant isolation for organization email templates (CLAUDE.md non-negotiable 6), as in AddWebhookSubscriptions.
        // screenhire_app keeps its default privileges: the API creates, changes and deletes templates, and the worker reads
        // them to send.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE email_templates ENABLE ROW LEVEL SECURITY;
            ALTER TABLE email_templates FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON email_templates
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON email_templates;
            ALTER TABLE email_templates NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE email_templates DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    built_in_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_organization_id_built_in_id",
                table: "email_templates",
                columns: new[] { "organization_id", "built_in_id" },
                unique: true,
                filter: "built_in_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_organization_id_created_at",
                table: "email_templates",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "email_templates");
        }
    }
}
