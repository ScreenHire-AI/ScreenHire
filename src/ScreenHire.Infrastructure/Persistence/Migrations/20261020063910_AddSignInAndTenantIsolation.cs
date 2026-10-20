using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInAndTenantIsolation : Migration
    {
        // Tenant isolation in the database (ADR-0008, CLAUDE.md non-negotiable 6). The API and worker connect as
        // screenhire_app: it neither owns the tables nor bypasses RLS, and FORCE makes the policies bind the owner role
        // too (superusers still bypass them; only migrations and the Development seed run as one).
        //
        // app.tenant_id and app.user_id are set transaction-locally by TenantSessionInterceptor. A reverted local
        // setting reads back as '' rather than NULL, hence NULLIF before the uuid cast: unset means no rows, never an
        // error. A later tenant-owned table copies the jobs block below in its own migration.
        private const string EnableRowLevelSecurity = """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'screenhire_app') THEN
                    CREATE ROLE screenhire_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                END IF;
            END
            $$;

            GRANT USAGE ON SCHEMA public TO screenhire_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO screenhire_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO screenhire_app;
            -- Tables and sequences that later migrations create (as the same owner) get the same grants.
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO screenhire_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO screenhire_app;
            -- Only the owner migrates.
            REVOKE ALL ON "__EFMigrationsHistory" FROM screenhire_app;

            -- jobs: one organization's rows only, for reads and writes.
            ALTER TABLE jobs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON jobs
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            -- organization_members: writes stay inside the tenant; a user may also read their own memberships, which is
            -- how the API finds a signed-in user's organization before it knows the tenant.
            ALTER TABLE organization_members ENABLE ROW LEVEL SECURITY;
            ALTER TABLE organization_members FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON organization_members
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY own_memberships ON organization_members FOR SELECT
                USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);

            -- organizations is the tenant registry. Anyone may read it (careers pages resolve a slug, onboarding checks
            -- that a slug is free), so its columns must stay safe to show publicly. Anyone may create one (onboarding);
            -- only the organization itself may change or delete its row.
            ALTER TABLE organizations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE organizations FORCE ROW LEVEL SECURITY;
            CREATE POLICY public_read ON organizations FOR SELECT USING (true);
            CREATE POLICY create_any ON organizations FOR INSERT WITH CHECK (true);
            CREATE POLICY tenant_update ON organizations FOR UPDATE
                USING (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_delete ON organizations FOR DELETE
                USING (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY tenant_update ON organizations;
            DROP POLICY tenant_delete ON organizations;
            DROP POLICY create_any ON organizations;
            DROP POLICY public_read ON organizations;
            ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE organizations DISABLE ROW LEVEL SECURITY;

            DROP POLICY own_memberships ON organization_members;
            DROP POLICY tenant_isolation ON organization_members;

            DROP POLICY tenant_isolation ON jobs;
            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs DISABLE ROW LEVEL SECURITY;

            ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM screenhire_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM screenhire_app;
            REVOKE ALL ON ALL TABLES IN SCHEMA public FROM screenhire_app;
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM screenhire_app;
            REVOKE USAGE ON SCHEMA public FROM screenhire_app;
            DROP ROLE IF EXISTS screenhire_app;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "magic_link_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_magic_link_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_members_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_magic_link_tokens_email_created_at",
                table: "magic_link_tokens",
                columns: new[] { "email", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_magic_link_tokens_token_hash",
                table: "magic_link_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_organization_id_user_id",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_user_id",
                table: "organization_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "magic_link_tokens");

            migrationBuilder.DropTable(
                name: "organization_members");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
