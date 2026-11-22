using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOptions : Migration
    {
        // Tenant row-level security like every tenant table (AddSignInAndTenantIsolation). A member's interview hours belong to
        // no job, so the table has no job_scope policy, as member_preferences has none: availability reads every interviewer's
        // hours, and the routes that return them return only the caller's own.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE interview_hours ENABLE ROW LEVEL SECURITY;
            ALTER TABLE interview_hours FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON interview_hours
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "another_time_asked_at",
                table: "interview_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "candidate_note",
                table: "interview_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "candidate_times",
                table: "interview_requests",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "candidate_times_at",
                table: "interview_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extra_times",
                table: "interview_requests",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "hours",
                table: "interview_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "option",
                table: "interview_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every interview offered before there were options was pick from our times, from calendars.
                defaultValue: "OurTimes");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "option_changed_at",
                table: "interview_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposed_times",
                table: "interview_requests",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "team_note",
                table: "interview_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "use_blocks",
                table: "interview_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "invited_revision",
                table: "interview_attendees",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "interview_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    weekly = table.Column<string>(type: "jsonb", nullable: true),
                    buffer_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_per_day = table.Column<int>(type: "integer", nullable: true),
                    feed_secret = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interview_hours", x => x.id);
                    table.ForeignKey(
                        name: "fk_interview_hours_organization_members_member_id",
                        column: x => x.member_id,
                        principalTable: "organization_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interview_hours_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_interview_hours_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interview_hours_member_id",
                table: "interview_hours",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_hours_organization_id_member_id",
                table: "interview_hours",
                columns: new[] { "organization_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_hours_organization_id_user_id",
                table: "interview_hours",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_hours_user_id",
                table: "interview_hours",
                column: "user_id");

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interview_hours");

            migrationBuilder.DropColumn(
                name: "another_time_asked_at",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "candidate_note",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "candidate_times",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "candidate_times_at",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "extra_times",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "hours",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "option",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "option_changed_at",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "proposed_times",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "team_note",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "use_blocks",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "invited_revision",
                table: "interview_attendees");
        }
    }
}
