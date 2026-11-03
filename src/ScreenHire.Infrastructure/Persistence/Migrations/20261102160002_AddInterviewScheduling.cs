using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Interview scheduling (docs/architecture/interview-scheduling.md): <c>interview_requests</c> holds the interview
    /// offered to an application's candidate and its booked time, <c>interview_attendees</c> one row per interviewer.
    /// </summary>
    public partial class AddInterviewScheduling : Migration
    {
        // Row-level security like every tenant table (AddSignInAndTenantIsolation). Both tables hang under an application,
        // so both also get the restrictive job_scope policy of AddJobAccess: a member on Only jobs they're on reads an
        // interview only through a job whose hiring team they are on.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE interview_requests ENABLE ROW LEVEL SECURITY;
            ALTER TABLE interview_requests FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON interview_requests
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON interview_requests AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));

            ALTER TABLE interview_attendees ENABLE ROW LEVEL SECURITY;
            ALTER TABLE interview_attendees FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON interview_attendees
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON interview_attendees AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            """;

        // The earlier model cannot read the timeline entries this one adds, so a rollback deletes them, as AddTalentPool does
        // for its own. Row-level security is forced on application_events and matches no row without a tenant, so FORCE is
        // off around it.
        private const string DeleteInterviewEntries = """
            ALTER TABLE application_events NO FORCE ROW LEVEL SECURITY;
            DELETE FROM application_events WHERE type IN ('InterviewInvited', 'InterviewBooked', 'InterviewRescheduled', 'InterviewCancelled');
            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interview_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    day_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    meeting_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_candidate = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_requests", x => x.id);
                    table.UniqueConstraint("ak_interview_requests_organization_id_id_application_id", x => new { x.organization_id, x.id, x.application_id });
                    table.ForeignKey(
                        name: "fk_interview_requests_applications_organization_id_application",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interview_requests_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interview_attendees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_organizer = table.Column<bool>(type: "boolean", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sync_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sync_problem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_attendees", x => x.id);
                    table.ForeignKey(
                        name: "fk_interview_attendees_applications_organization_id_applicatio",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interview_attendees_interview_requests_organization_id_inte",
                        columns: x => new { x.organization_id, x.interview_id, x.application_id },
                        principalTable: "interview_requests",
                        principalColumns: new[] { "organization_id", "id", "application_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interview_attendees_organization_id_application_id",
                table: "interview_attendees",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_interview_attendees_organization_id_interview_id_user_id",
                table: "interview_attendees",
                columns: new[] { "organization_id", "interview_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_attendees_organization_id_interview_id_applicatio",
                table: "interview_attendees",
                columns: new[] { "organization_id", "interview_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_interview_attendees_organization_id_user_id",
                table: "interview_attendees",
                columns: new[] { "organization_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_interview_requests_organization_id_application_id",
                table: "interview_requests",
                columns: new[] { "organization_id", "application_id" },
                unique: true,
                filter: "status IN ('Open', 'Booked')");

            migrationBuilder.CreateIndex(
                name: "ix_interview_requests_organization_id_application_id_source_key",
                table: "interview_requests",
                columns: new[] { "organization_id", "application_id", "source_key" },
                unique: true,
                filter: "source_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_interview_requests_organization_id_job_id",
                table: "interview_requests",
                columns: new[] { "organization_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_interview_requests_organization_id_starts_at",
                table: "interview_requests",
                columns: new[] { "organization_id", "starts_at" },
                filter: "status = 'Booked'");

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeleteInterviewEntries);

            migrationBuilder.DropTable(
                name: "interview_attendees");

            migrationBuilder.DropTable(
                name: "interview_requests");
        }
    }
}
