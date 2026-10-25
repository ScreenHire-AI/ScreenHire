using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomRoles : Migration
    {
        // Tenant isolation for custom roles (CLAUDE.md non-negotiable 6), as in jobs. The application role keeps UPDATE
        // and DELETE from the default privileges: roles are renamed, edited and deleted. Existing members and invitations
        // get a NULL custom_role_id and keep their built-in role, so their access does not change (ADR-0014).
        private const string EnableRowLevelSecurity = """
            ALTER TABLE custom_roles ENABLE ROW LEVEL SECURITY;
            ALTER TABLE custom_roles FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON custom_roles
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON custom_roles;
            ALTER TABLE custom_roles NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE custom_roles DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "custom_role_id",
                table: "organization_members",
                type: "uuid",
                nullable: true);

            // No foreign key: see OrganizationInvitationConfiguration.
            migrationBuilder.AddColumn<Guid>(
                name: "custom_role_id",
                table: "organization_invitations",
                type: "uuid",
                nullable: true);

            // The log keeps ids and names as they were, with no foreign key, like the member and actor ids beside them.
            migrationBuilder.AddColumn<Guid>(
                name: "custom_role_id",
                table: "membership_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_role_name",
                table: "membership_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_custom_role_id",
                table: "membership_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_custom_role_name",
                table: "membership_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false),
                    copied_from = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_roles", x => x.id);
                    table.UniqueConstraint("ak_custom_roles_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_custom_roles_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_organization_id_custom_role_id",
                table: "organization_members",
                columns: new[] { "organization_id", "custom_role_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_members_custom_role",
                table: "organization_members",
                sql: "(role = 'Custom') = (custom_role_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_invitations_custom_role",
                table: "organization_invitations",
                sql: "(role = 'Custom') = (custom_role_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_custom_roles_organization_id_normalized_name",
                table: "custom_roles",
                columns: new[] { "organization_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_organization_members_custom_roles_organization_id_custom_ro",
                table: "organization_members",
                columns: new[] { "organization_id", "custom_role_id" },
                principalTable: "custom_roles",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropForeignKey(
                name: "fk_organization_members_custom_roles_organization_id_custom_ro",
                table: "organization_members");

            migrationBuilder.DropTable(
                name: "custom_roles");

            migrationBuilder.DropIndex(
                name: "ix_organization_members_organization_id_custom_role_id",
                table: "organization_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_members_custom_role",
                table: "organization_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_invitations_custom_role",
                table: "organization_invitations");

            migrationBuilder.DropColumn(
                name: "custom_role_id",
                table: "organization_members");

            migrationBuilder.DropColumn(
                name: "custom_role_id",
                table: "organization_invitations");

            migrationBuilder.DropColumn(
                name: "custom_role_id",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "custom_role_name",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "previous_custom_role_id",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "previous_custom_role_name",
                table: "membership_events");
        }
    }
}
