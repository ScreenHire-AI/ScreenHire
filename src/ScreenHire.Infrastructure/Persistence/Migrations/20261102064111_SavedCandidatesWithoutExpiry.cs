using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Saved candidates (docs/architecture/talent-pool.md) stay until the team removes them or the candidate asks, so the
    /// permission period goes: the settings table and each application's copy of the period. A saved candidate's note
    /// becomes the entry's own, editable, and starts as the note written when they were saved.
    /// </summary>
    public partial class SavedCandidatesWithoutExpiry : Migration
    {
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

            // The period was 12 months for everyone until the setting went; applications that carried a permission get it back.
            migrationBuilder.Sql(RestoreConsentMonths);
        }
    }
}
