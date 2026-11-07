using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewNewTimesAndReminders : Migration
    {
        // An existing request's lookup span is its window. interview_requests has FORCE ROW LEVEL SECURITY, which binds the
        // owner running this migration too: with no app.tenant_id the UPDATE would match no rows and every span would stay
        // at the column default. So FORCE is lifted around the UPDATE (transactional DDL: a failure rolls all of it back), as
        // AddHiringModel does for jobs.
        private const string BackfillLookupSpan = """
            ALTER TABLE interview_requests NO FORCE ROW LEVEL SECURITY;
            UPDATE interview_requests SET lookup_start = window_start, lookup_end = window_end;
            ALTER TABLE interview_requests FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lookup_end",
                table: "interview_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lookup_start",
                table: "interview_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql(BackfillLookupSpan);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "new_times_offered_at",
                table: "interview_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminded_for_start",
                table: "interview_attendees",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lookup_end",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "lookup_start",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "new_times_offered_at",
                table: "interview_requests");

            migrationBuilder.DropColumn(
                name: "reminded_for_start",
                table: "interview_attendees");
        }
    }
}
