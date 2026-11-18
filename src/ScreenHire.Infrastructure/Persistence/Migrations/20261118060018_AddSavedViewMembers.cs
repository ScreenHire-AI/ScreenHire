using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sharing a view becomes an offer (docs/architecture/saved-views.md): <c>saved_view_members</c> holds the shared views each
    /// person added to their own sidebar, and <c>shared_at</c> says when a view was shared, for the New mark.
    /// </summary>
    public partial class AddSavedViewMembers : Migration
    {
        // Tenant row-level security like every tenant table (AddSignInAndTenantIsolation). The table has no job_id, so it needs no
        // job_scope policy: the API returns a row's view only to a member who may see it.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE saved_view_members ENABLE ROW LEVEL SECURITY;
            ALTER TABLE saved_view_members FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON saved_view_members
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // Every shared view sat in every member's sidebar, so each member who is not its owner keeps it there: nobody loses a
        // view overnight. Members who cannot see a view get a row too, as they had it in their list the moment they could; what
        // they see is still checked when the list is read. The tables are forced, so with no tenant set the owner reads and
        // writes nothing unless the force is lifted around the copy. It is put back before the migration ends, and the copy
        // aborts it when a row was missed.
        private const string KeepSharedViewsInSidebars = """
            ALTER TABLE saved_filter_views NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE saved_view_members NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE organization_members NO FORCE ROW LEVEL SECURITY;
            UPDATE saved_filter_views SET shared_at = updated_at WHERE is_shared;
            INSERT INTO saved_view_members (view_id, user_id, organization_id, position, added_at)
                SELECT v.id, m.user_id, v.organization_id,
                       row_number() OVER (PARTITION BY m.user_id ORDER BY v.created_at, v.id) - 1,
                       now()
                FROM saved_filter_views v
                JOIN organization_members m ON m.organization_id = v.organization_id AND m.user_id <> v.owner_user_id
                WHERE v.is_shared;
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM saved_filter_views v
                    JOIN organization_members m ON m.organization_id = v.organization_id AND m.user_id <> v.owner_user_id
                    WHERE v.is_shared
                      AND NOT EXISTS (SELECT 1 FROM saved_view_members s WHERE s.view_id = v.id AND s.user_id = m.user_id)) THEN
                    RAISE EXCEPTION 'The shared views were not all kept in their members'' sidebars.';
                END IF;
            END $$;
            ALTER TABLE organization_members FORCE ROW LEVEL SECURITY;
            ALTER TABLE saved_view_members FORCE ROW LEVEL SECURITY;
            ALTER TABLE saved_filter_views FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shared_at",
                table: "saved_filter_views",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "saved_view_members",
                columns: table => new
                {
                    view_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_view_members", x => new { x.view_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_saved_view_members_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_view_members_saved_filter_views_view_id",
                        column: x => x.view_id,
                        principalTable: "saved_filter_views",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_view_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_view_members_organization_id_user_id_position",
                table: "saved_view_members",
                columns: new[] { "organization_id", "user_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_view_members_user_id",
                table: "saved_view_members",
                column: "user_id");

            migrationBuilder.Sql(EnableRowLevelSecurity);
            migrationBuilder.Sql(KeepSharedViewsInSidebars);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_view_members");

            migrationBuilder.DropColumn(
                name: "shared_at",
                table: "saved_filter_views");
        }
    }
}
