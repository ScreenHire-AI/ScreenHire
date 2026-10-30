using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScorecardRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_application_scorecards_organization_id_application_id_stage",
                table: "application_scorecards");

            migrationBuilder.AddColumn<string>(
                name: "scorecard_template_id",
                table: "application_tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scorecard_name",
                table: "application_scorecards",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scorecard_template_id",
                table: "application_scorecards",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_scorecards_organization_id_application_id_stage",
                table: "application_scorecards",
                columns: new[] { "organization_id", "application_id", "stage_id", "reviewer_user_id", "scorecard_template_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // A review is now done when its scorecard is submitted, and nobody ticks one by hand (ADR-0017 decision 11).
            // Finish the open reviews a scorecard already answered: one their assignee (anyone's, for a review for the
            // whole team) gave since the review was assigned, at the first such scorecard's time and by its reviewer.
            migrationBuilder.Sql("""
                UPDATE application_tasks AS t
                SET completed_at = answered.submitted_at, completed_by_user_id = answered.reviewer_user_id
                FROM (
                    SELECT DISTINCT ON (task.id) task.id AS task_id, s.submitted_at, s.reviewer_user_id
                    FROM application_tasks AS task
                    JOIN application_scorecards AS s
                      ON s.organization_id = task.organization_id
                     AND s.application_id = task.application_id
                     AND s.submitted_at >= task.created_at
                     AND (task.assignee_user_id IS NULL OR s.reviewer_user_id = task.assignee_user_id)
                    WHERE task.kind = 'Review' AND task.completed_at IS NULL
                    ORDER BY task.id, s.submitted_at
                ) AS answered
                WHERE t.id = answered.task_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_application_scorecards_organization_id_application_id_stage",
                table: "application_scorecards");

            migrationBuilder.DropColumn(
                name: "scorecard_template_id",
                table: "application_tasks");

            migrationBuilder.DropColumn(
                name: "scorecard_name",
                table: "application_scorecards");

            migrationBuilder.DropColumn(
                name: "scorecard_template_id",
                table: "application_scorecards");

            migrationBuilder.CreateIndex(
                name: "ix_application_scorecards_organization_id_application_id_stage",
                table: "application_scorecards",
                columns: new[] { "organization_id", "application_id", "stage_id", "reviewer_user_id" },
                unique: true);
        }
    }
}
