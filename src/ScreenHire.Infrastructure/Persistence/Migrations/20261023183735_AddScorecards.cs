using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Reviewer scorecards (workflow-engine.md §8): the <c>application_scorecards</c> table, and the system templates'
    /// new versions with a default scorecard on their review stages ("Software engineer" version 3, "Hourly role"
    /// version 2).
    /// </summary>
    public partial class AddScorecards : Migration
    {
        // Row-level security like every tenant table (AddSignInAndTenantIsolation). A submitted scorecard is final, the
        // record of one person's judgement, so the application role may add and read rows but never change or delete one.
        // System template versions with default scorecards: row-level security is forced on the template tables and its
        // policies let nobody write a row without an organization, so FORCE is off around the inserts
        // (AddSoftwareEngineerTemplateVersion2). Each WHERE clause pins the stages being given a scorecard, so an insert
        // does nothing if the version it copies is not as expected. The criteria are about the job only (CLAUDE.md
        // non-negotiable 3); the earlier versions stay as they were, and jobs copied from them see "update available".
        private const string EnableRowLevelSecurityAndTemplates = """
            ALTER TABLE application_scorecards ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_scorecards FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_scorecards
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON application_scorecards FROM screenhire_app;

            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000103', NULL, v.template_id, 3, 'Published',
                   jsonb_set(jsonb_set(jsonb_set(v.definition,
                       '{workflow,stages,1,scorecard}', '{"criteria":[{"id":"role_requirements","label":"Meets the role''s requirements","guidance":"The experience, qualifications or licences the job description asks for."},{"id":"relevant_experience","label":"Relevant experience","guidance":"Work they have done that is like this job''s work."}]}'::jsonb),
                       '{workflow,stages,2,scorecard}', '{"criteria":[{"id":"exercise_solution","label":"Solves the exercise","guidance":"Whether the solution does what the exercise asked."},{"id":"code_quality","label":"Code quality","guidance":"Readable, tested and well-structured code."},{"id":"design_choices","label":"Design choices","guidance":"The trade-offs they made and why they suit the problem."}]}'::jsonb),
                       '{workflow,stages,3,scorecard}', '{"criteria":[{"id":"role_skills","label":"Engineering skills the role needs","guidance":"The skills in the job description, shown in examples or answers."},{"id":"system_design","label":"System design","guidance":"How they would design a system like the ones this team builds."},{"id":"problem_solving","label":"Problem solving","guidance":"How they work through a problem taken from this job''s work."}]}'::jsonb),
                   '2026-10-23T18:00:00Z', '2026-10-23T18:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000102'
              AND v.definition #>> '{workflow,stages,1,id}' = 'screening'
              AND v.definition #>> '{workflow,stages,2,id}' = 'technical'
              AND v.definition #>> '{workflow,stages,3,id}' = 'interview';

            UPDATE hiring_templates SET latest_published_version = 3
            WHERE id = '0199e8a0-0000-7000-8000-000000000001'
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000103');

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000202', NULL, v.template_id, 2, 'Published',
                   jsonb_set(jsonb_set(v.definition,
                       '{workflow,stages,1,scorecard}', '{"criteria":[{"id":"role_requirements","label":"Meets the role''s requirements","guidance":"Licences, certificates or experience the job description asks for."},{"id":"relevant_experience","label":"Relevant experience","guidance":"Work they have done that is like this job''s tasks."}]}'::jsonb),
                       '{workflow,stages,2,scorecard}', '{"criteria":[{"id":"role_skills","label":"Skills the role needs","guidance":"The tasks of the job, shown in examples or a short trial."},{"id":"safe_working","label":"Knows how to work safely","guidance":"The safety steps this job''s tasks need."},{"id":"relevant_experience","label":"Relevant experience","guidance":"Work they have done that is like this job''s tasks."}]}'::jsonb),
                   '2026-10-23T18:00:00Z', '2026-10-23T18:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000201'
              AND v.definition #>> '{workflow,stages,1,id}' = 'screening'
              AND v.definition #>> '{workflow,stages,2,id}' = 'interview';

            UPDATE hiring_templates SET latest_published_version = 2
            WHERE id = '0199e8a0-0000-7000-8000-000000000002'
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000202');

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private const string RemoveTemplates = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            -- Jobs that used these versions keep their copied workflow; their source is set to NULL by the foreign key.
            DELETE FROM hiring_template_versions WHERE id IN ('0199e8a0-0000-7000-8000-000000000103', '0199e8a0-0000-7000-8000-000000000202');
            UPDATE hiring_templates SET latest_published_version = 2 WHERE id = '0199e8a0-0000-7000-8000-000000000001';
            UPDATE hiring_templates SET latest_published_version = 1 WHERE id = '0199e8a0-0000-7000-8000-000000000002';
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_scorecards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ratings = table.Column<string>(type: "jsonb", nullable: false),
                    recommendation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    workflow_version = table.Column<int>(type: "integer", nullable: false),
                    timeline_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_scorecards", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_scorecards_applications_organization_id_applica",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_scorecards_organization_id_application_id_stage",
                table: "application_scorecards",
                columns: new[] { "organization_id", "application_id", "stage_id", "reviewer_user_id" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurityAndTemplates);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveTemplates);

            migrationBuilder.DropTable(
                name: "application_scorecards");
        }
    }
}
