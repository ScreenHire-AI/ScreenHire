using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthConnections : Migration
    {
        // Tenant isolation for the codes and tokens of OAuth connections (CLAUDE.md non-negotiable 6), as in AddApiKeys.
        // The token endpoint and the MCP authentication handler read a code or token with the organization the string
        // names as the tenant, so this policy covers that lookup too. oauth_clients has none: a client registers before
        // anyone signs in, belongs to no organization, and holds only a name and redirect URIs.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE oauth_codes ENABLE ROW LEVEL SECURITY;
            ALTER TABLE oauth_codes FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON oauth_codes
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE oauth_tokens ENABLE ROW LEVEL SECURITY;
            ALTER TABLE oauth_tokens FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON oauth_tokens
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON oauth_tokens;
            ALTER TABLE oauth_tokens NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE oauth_tokens DISABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON oauth_codes;
            ALTER TABLE oauth_codes NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE oauth_codes DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "oauth_client_id",
                table: "api_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "oauth_clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    redirect_uris = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    code_challenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_oauth_codes_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_oauth_codes_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oauth_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_oauth_tokens_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_oauth_tokens_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_oauth_client_id",
                table: "api_keys",
                column: "oauth_client_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_clients_created_at",
                table: "oauth_clients",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_codes_api_key_id",
                table: "oauth_codes",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_codes_expires_at",
                table: "oauth_codes",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_codes_organization_id_code_hash",
                table: "oauth_codes",
                columns: new[] { "organization_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_api_key_id",
                table: "oauth_tokens",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_expires_at",
                table: "oauth_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_tokens_organization_id_token_hash",
                table: "oauth_tokens",
                columns: new[] { "organization_id", "token_hash" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_api_keys_o_auth_clients_oauth_client_id",
                table: "api_keys",
                column: "oauth_client_id",
                principalTable: "oauth_clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropForeignKey(
                name: "fk_api_keys_o_auth_clients_oauth_client_id",
                table: "api_keys");

            migrationBuilder.DropTable(
                name: "oauth_clients");

            migrationBuilder.DropTable(
                name: "oauth_codes");

            migrationBuilder.DropTable(
                name: "oauth_tokens");

            migrationBuilder.DropIndex(
                name: "ix_api_keys_oauth_client_id",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "oauth_client_id",
                table: "api_keys");
        }
    }
}
