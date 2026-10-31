using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlack : Migration
    {
        // Every Slack table is tenant-owned: the same tenant_isolation policy as every other (docs/architecture/sign-in-and-tenancy.md).
        private const string EnableRowLevelSecurity = """
            ALTER TABLE slack_installations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE slack_installations FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON slack_installations
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE slack_account_links ENABLE ROW LEVEL SECURITY;
            ALTER TABLE slack_account_links FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON slack_account_links
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE slack_messages ENABLE ROW LEVEL SECURITY;
            ALTER TABLE slack_messages FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON slack_messages
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE slack_sandbox_messages ENABLE ROW LEVEL SECURITY;
            ALTER TABLE slack_sandbox_messages FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON slack_sandbox_messages
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON slack_sandbox_messages;
            ALTER TABLE slack_sandbox_messages NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE slack_sandbox_messages DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON slack_messages;
            ALTER TABLE slack_messages NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE slack_messages DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON slack_account_links;
            ALTER TABLE slack_account_links NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE slack_account_links DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON slack_installations;
            ALTER TABLE slack_installations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE slack_installations DISABLE ROW LEVEL SECURITY;
            """;
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "slack_installations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    team_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bot_user_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    protected_bot_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    channel_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    channel_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    events = table.Column<string[]>(type: "text[]", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    connected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    problem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    problem_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slack_installations", x => x.id);
                    table.UniqueConstraint("ak_slack_installations_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_slack_installations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "slack_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slack_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_slack_messages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "slack_sandbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slack_sandbox_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_slack_sandbox_messages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "slack_account_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_user_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    slack_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mentions = table.Column<bool>(type: "boolean", nullable: false),
                    tasks = table.Column<bool>(type: "boolean", nullable: false),
                    problem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    problem_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slack_account_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_slack_account_links_slack_installations_organization_id_ins",
                        columns: x => new { x.organization_id, x.installation_id },
                        principalTable: "slack_installations",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_slack_account_links_organization_id_installation_id",
                table: "slack_account_links",
                columns: new[] { "organization_id", "installation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_slack_account_links_organization_id_slack_user_id",
                table: "slack_account_links",
                columns: new[] { "organization_id", "slack_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slack_account_links_organization_id_user_id",
                table: "slack_account_links",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slack_installations_organization_id",
                table: "slack_installations",
                column: "organization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slack_messages_organization_id_at",
                table: "slack_messages",
                columns: new[] { "organization_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_slack_messages_organization_id_idempotency_key",
                table: "slack_messages",
                columns: new[] { "organization_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slack_sandbox_messages_organization_id_posted_at",
                table: "slack_sandbox_messages",
                columns: new[] { "organization_id", "posted_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "slack_account_links");

            migrationBuilder.DropTable(
                name: "slack_messages");

            migrationBuilder.DropTable(
                name: "slack_sandbox_messages");

            migrationBuilder.DropTable(
                name: "slack_installations");
        }
    }
}
