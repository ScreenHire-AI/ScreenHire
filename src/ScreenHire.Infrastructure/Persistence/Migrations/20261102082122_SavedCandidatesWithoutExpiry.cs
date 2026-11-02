using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Saved candidates (docs/architecture/talent-pool.md) stay until the team removes them or the candidate asks, so the
    /// permission period goes: the settings table and each application's copy of the period. A saved candidate's note
    /// becomes the entry's own, editable, and starts as the note written when they were saved. A permission that had already
    /// run out stays ended: without its period it would read as given and not withdrawn, so it would come back.
    /// </summary>
    public partial class SavedCandidatesWithoutExpiry : Migration
    {
        // Runs first, while the periods exist. The rule is the old JobApplication.FutureRolesUntil: the shorter of the period
        // when it was given and the organization's period, 12 months when it set none. A consent with no period never counted
        // as a permission, so it ends on the day it was given (LEAST alone would skip the NULL). The last daily sweep may not
        // have seen everyone, so anyone still saved on a lapsed permission leaves as the sweep took them, with Expired on
        // their Activity.
        // Row-level security is forced on all four tables and the migration has no tenant, so FORCE is off around it. The
        // settings table is dropped next, so only the other three get it back.
        private const string EndLapsedPermissions = """
            ALTER TABLE applications NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_settings NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_entries NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE application_events NO FORCE ROW LEVEL SECURITY;

            WITH permissions AS (
                SELECT application.id,
                       application.future_roles_consent_given_at + make_interval(months => LEAST(
                           COALESCE(application.future_roles_consent_months, 0), COALESCE(settings.permission_months, 12))) AS ended_at
                FROM applications AS application
                LEFT JOIN talent_pool_settings AS settings ON settings.organization_id = application.organization_id
                WHERE application.future_roles_consent_given_at IS NOT NULL
                  AND application.future_roles_withdrawn_at IS NULL
            )
            UPDATE applications AS application
            SET future_roles_withdrawn_at = permissions.ended_at
            FROM permissions
            WHERE permissions.id = application.id AND permissions.ended_at <= now();

            WITH left_pool AS (
                DELETE FROM talent_pool_entries AS entry
                USING applications AS application
                WHERE application.id = entry.application_id AND application.future_roles_withdrawn_at IS NOT NULL
                RETURNING entry.organization_id, entry.application_id
            )
            INSERT INTO application_events (id, organization_id, application_id, type, data, occurred_at)
            SELECT gen_random_uuid(), organization_id, application_id, 'LeftTalentPool', '{"poolExit":"Expired"}'::jsonb, now()
            FROM left_pool;

            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_entries FORCE ROW LEVEL SECURITY;
            ALTER TABLE applications FORCE ROW LEVEL SECURITY;
            """;

        // The backfill runs as the table owner with no tenant set, and row-level security is forced on both tables, so the
        // join would match nothing and every note would stay empty. FORCE is off around it (as AddTalentPool's rollback
        // does for application_events) and back on after.
        private const string BackfillNotes = """
            ALTER TABLE talent_pool_entries NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE application_events NO FORCE ROW LEVEL SECURITY;
            UPDATE talent_pool_entries AS entry
            SET note = NULLIF(BTRIM(event.data ->> 'note'), '')
            FROM application_events AS event
            WHERE event.id = entry.kept_event_id;
            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_entries FORCE ROW LEVEL SECURITY;
            """;

        // Row-level security is forced on applications too, so the same applies to the rollback's update.
        private const string RestoreConsentMonths = """
            ALTER TABLE applications NO FORCE ROW LEVEL SECURITY;
            UPDATE applications SET future_roles_consent_months = 12 WHERE future_roles_consent_given_at IS NOT NULL;
            ALTER TABLE applications FORCE ROW LEVEL SECURITY;
            """;

        private const string EnableSettingsRowLevelSecurity = """
            ALTER TABLE talent_pool_settings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE talent_pool_settings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON talent_pool_settings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EndLapsedPermissions);

            migrationBuilder.DropTable(
                name: "talent_pool_settings");

            migrationBuilder.DropColumn(
                name: "future_roles_consent_months",
                table: "applications");

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "talent_pool_entries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.Sql(BackfillNotes);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "note",
                table: "talent_pool_entries");

            migrationBuilder.AddColumn<int>(
                name: "future_roles_consent_months",
                table: "applications",
                type: "integer",
                nullable: true);

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

            migrationBuilder.Sql(EnableSettingsRowLevelSecurity);

            // The real periods are gone, so this is not a faithful rollback: every permission gets the default 12 months, an
            // organization's own setting does not return, and the permissions Up ended stay ended.
            migrationBuilder.Sql(RestoreConsentMonths);
        }
    }
}
