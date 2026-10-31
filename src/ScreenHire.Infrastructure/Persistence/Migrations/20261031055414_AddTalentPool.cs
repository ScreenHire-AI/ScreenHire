using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The talent pool (docs/architecture/talent-pool.md): an application can be kept in mind, which ends it without a
    /// rejection, and carries the candidate's own permission for future roles; <c>talent_pool_entries</c> lists who is in
    /// the pool, one per candidate, and <c>talent_pool_settings</c> holds how long a permission lasts.
    /// </summary>
    public partial class AddTalentPool : Migration
    {
        // Row-level security like every tenant table (AddSignInAndTenantIsolation). An entry belongs to the application it
        // was kept from, so it also gets the restrictive job_scope policy of the rows under an application (AddJobAccess).
        private const string EnableRowLevelSecurity = """
            ALTER TABLE talent_pool_entries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_entries FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON talent_pool_entries
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON talent_pool_entries AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));

            ALTER TABLE talent_pool_settings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_settings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON talent_pool_settings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // The earlier model cannot read the timeline entries this one adds, so a rollback deletes them, as
        // AddApplicationNotes does for its own. The record of who kept whom in mind is lost on a rollback. Row-level
        // security is forced on application_events and matches no row without a tenant, so FORCE is off around it.
        private const string DeleteTalentPoolEntries = """
            ALTER TABLE application_events NO FORCE ROW LEVEL SECURITY;
            DELETE FROM application_events WHERE type IN ('KeptInMind', 'LeftTalentPool', 'InvitedToApply');
            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "future_roles_consent_given_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "future_roles_consent_months",
                table: "applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "future_roles_withdrawn_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "kept_in_mind_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "talent_pool_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kept_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kept_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kept_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_invited_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_talent_pool_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_talent_pool_entries_applications_organization_id_applicatio",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_talent_pool_entries_candidates_organization_id_candidate_id",
                        columns: x => new { x.organization_id, x.candidate_id },
                        principalTable: "candidates",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "talent_pool_settings",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_months = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_talent_pool_settings", x => x.organization_id);
                    table.ForeignKey(
                        name: "fk_talent_pool_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_entries_organization_id_application_id",
                table: "talent_pool_entries",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_entries_organization_id_candidate_id",
                table: "talent_pool_entries",
                columns: new[] { "organization_id", "candidate_id" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeleteTalentPoolEntries);

            migrationBuilder.DropTable(
                name: "talent_pool_entries");

            migrationBuilder.DropTable(
                name: "talent_pool_settings");

            migrationBuilder.DropColumn(
                name: "future_roles_consent_given_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "future_roles_consent_months",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "future_roles_withdrawn_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "kept_in_mind_at",
                table: "applications");
        }
    }
}
