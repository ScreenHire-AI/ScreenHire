using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Questionnaire pages and saving to finish later (apply-flow.md §7 and §8): the organization's questions per page
    /// (8 for every organization, as for a new one), the <c>application_drafts</c> table, a follow-up request's saved
    /// answers, and the submission's time per page with the facts about a draft it was sent from.
    /// </summary>
    public partial class AddApplicationDrafts : Migration
    {
        // Row-level security like every tenant table (AddSignInAndTenantIsolation), and the restrictive job scope every
        // table under a job has (AddJobAccess). screenhire_app keeps its default privileges: the API saves, reads and
        // deletes drafts, and the worker's daily sweep deletes the expired ones.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE application_drafts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_drafts FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_drafts
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON application_drafts AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "carried_over_question_ids",
                table: "submission_observations",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "page_times",
                table: "submission_observations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "resumed_from_draft",
                table: "submission_observations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "draft_answers",
                table: "questionnaire_requests",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "draft_expires_at",
                table: "questionnaire_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "draft_saved_at",
                table: "questionnaire_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "questions_per_page",
                table: "organizations",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.CreateTable(
                name: "application_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    accommodation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    ai_use_declaration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ai_use_details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_drafts_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_drafts_organization_id_email_created_at",
                table: "application_drafts",
                columns: new[] { "organization_id", "email", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_application_drafts_organization_id_expires_at",
                table: "application_drafts",
                columns: new[] { "organization_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_application_drafts_organization_id_job_id_email",
                table: "application_drafts",
                columns: new[] { "organization_id", "job_id", "email" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_drafts");

            migrationBuilder.DropColumn(
                name: "carried_over_question_ids",
                table: "submission_observations");

            migrationBuilder.DropColumn(
                name: "page_times",
                table: "submission_observations");

            migrationBuilder.DropColumn(
                name: "resumed_from_draft",
                table: "submission_observations");

            migrationBuilder.DropColumn(
                name: "draft_answers",
                table: "questionnaire_requests");

            migrationBuilder.DropColumn(
                name: "draft_expires_at",
                table: "questionnaire_requests");

            migrationBuilder.DropColumn(
                name: "draft_saved_at",
                table: "questionnaire_requests");

            migrationBuilder.DropColumn(
                name: "questions_per_page",
                table: "organizations");
        }
    }
}
