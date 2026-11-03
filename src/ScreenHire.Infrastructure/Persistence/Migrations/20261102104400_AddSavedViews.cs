using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Views people save on the candidate tables (docs/architecture/saved-views.md): <c>saved_filter_views</c>, and the
    /// built-in views each member hid, kept on their membership beside the notification settings.
    /// </summary>
    public partial class AddSavedViews : Migration
    {
        // Tenant row-level security like every tenant table (AddSignInAndTenantIsolation), and job_scope for a view of one job
        // (ADR-0015). A candidates view has no job, so the policy passes it; the API checks the jobs it picks.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE saved_filter_views ENABLE ROW LEVEL SECURITY;
            ALTER TABLE saved_filter_views FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON saved_filter_views
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON saved_filter_views AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (job_id IS NULL OR app_sees_job(organization_id, job_id))
                WITH CHECK (job_id IS NULL OR app_sees_job(organization_id, job_id));
            """;

        // One name for one person and table, without regard to case. Deleted rows are out of it, so a name can be reused while
        // Undo is on offer. An expression index, so it is SQL rather than part of the model. Candidates views have no job_id,
        // which a plain unique index would treat as all different.
        private const string UniqueNames = """
            CREATE UNIQUE INDEX ux_saved_filter_views_name ON saved_filter_views
                (organization_id, owner_user_id, scope, coalesce(job_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(name))
                WHERE deleted_at IS NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "views",
                table: "organization_members",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{\"hiddenBuiltIns\":[]}'::jsonb");

            migrationBuilder.CreateTable(
                name: "saved_filter_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_filter_views", x => x.id);
                    table.CheckConstraint("ck_saved_filter_views_job", "(scope = 'Job') = (job_id IS NOT NULL)");
                    table.CheckConstraint("ck_saved_filter_views_scope", "scope IN ('Candidates', 'Job')");
                    table.ForeignKey(
                        name: "fk_saved_filter_views_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_filter_views_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_filter_views_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_filter_views_organization_id_is_shared_owner_user_id",
                table: "saved_filter_views",
                columns: new[] { "organization_id", "is_shared", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_filter_views_organization_id_job_id",
                table: "saved_filter_views",
                columns: new[] { "organization_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_filter_views_owner_user_id",
                table: "saved_filter_views",
                column: "owner_user_id");

            migrationBuilder.Sql(UniqueNames);
            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_filter_views");

            migrationBuilder.DropColumn(
                name: "views",
                table: "organization_members");
        }
    }
}
