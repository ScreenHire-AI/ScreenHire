using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeys : Migration
    {
        // Tenant isolation for API keys (CLAUDE.md non-negotiable 6), as in AddSignInAndTenantIsolation. Authentication
        // reads a key with the organization the key names as the tenant, so this policy covers that lookup too. Keys are
        // revoked, never deleted, so the list keeps who had access: screenhire_app has no DELETE.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE api_keys ENABLE ROW LEVEL SECURITY;
            ALTER TABLE api_keys FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON api_keys
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE DELETE ON api_keys FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON api_keys;
            ALTER TABLE api_keys NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE api_keys DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    secret_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    permits_per_minute = table.Column<int>(type: "integer", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_api_keys_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_created_by_user_id",
                table: "api_keys",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_organization_id_created_at",
                table: "api_keys",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_secret_hash",
                table: "api_keys",
                column: "secret_hash",
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "api_keys");
        }
    }
}
