using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMembers : Migration
    {
        // Tenant isolation for invitations and the membership log (CLAUDE.md non-negotiable 6), as in jobs. The log is
        // append-only for audit, like application_events: screenhire_app may read and insert, never change or delete.
        // Accepting an invitation sets the tenant from the organization its token names before it reads the row.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE organization_invitations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE organization_invitations FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON organization_invitations
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE membership_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE membership_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON membership_events
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON membership_events FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON membership_events;
            ALTER TABLE membership_events NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE membership_events DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS tenant_isolation ON organization_invitations;
            ALTER TABLE organization_invitations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE organization_invitations DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "active_organization_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "membership_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_membership_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_active_organization_id",
                table: "users",
                column: "active_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_events_organization_id_occurred_at",
                table: "membership_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_invitations_organization_id_email",
                table: "organization_invitations",
                columns: new[] { "organization_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_invitations_organization_id_token_hash",
                table: "organization_invitations",
                columns: new[] { "organization_id", "token_hash" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_users_organizations_active_organization_id",
                table: "users",
                column: "active_organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropForeignKey(
                name: "fk_users_organizations_active_organization_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "membership_events");

            migrationBuilder.DropTable(
                name: "organization_invitations");

            migrationBuilder.DropIndex(
                name: "ix_users_active_organization_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "active_organization_id",
                table: "users");
        }
    }
}
