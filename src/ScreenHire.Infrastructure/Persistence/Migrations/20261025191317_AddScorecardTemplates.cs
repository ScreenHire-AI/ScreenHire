using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Scorecards v0.3 (ADR-0013, workflow-engine.md section 8): <c>scorecard_templates</c> holds each organization's own
    /// scorecards next to the built-ins in code (<c>BuiltInScorecards</c>); a hiring template's stage names one, and a
    /// job keeps its own copy of the questions. Submitted scorecards gain <c>answers</c>, and review tasks the stage they
    /// were assigned in. Every stored v0.2 scorecard (a list of criteria) is rewritten here, in this transaction.
    /// </summary>
    public partial class AddScorecardTemplates : Migration
    {
        // The built-ins' v0.2 criteria, exactly as AddScorecards and the default workflow stored them, so a stored scorecard
        // equal to one (jsonb equality ignores key order) points at the built-in instead of becoming a copy in the library.
        private const string BuiltIns = """
            CREATE TEMP TABLE builtin_scorecards (id text PRIMARY KEY, name text NOT NULL, criteria jsonb NOT NULL);
            INSERT INTO builtin_scorecards VALUES
            ('screening', 'Screening', '[{"id":"role_requirements","label":"Meets the role''s requirements","guidance":"The experience, qualifications or licences the job description asks for."},{"id":"relevant_experience","label":"Relevant experience","guidance":"Work they have done that is like this job''s work."}]'),
            ('interview', 'Interview', '[{"id":"role_skills","label":"Skills the role needs","guidance":"The skills in the job description, shown in examples or answers."},{"id":"problem_solving","label":"Problem solving","guidance":"How they work through a problem taken from this job''s work."},{"id":"work_knowledge","label":"Knowledge of the work","guidance":"What they know about the tools, methods or rules this job uses."}]'),
            ('technical-exercise', 'Technical exercise', '[{"id":"exercise_solution","label":"Solves the exercise","guidance":"Whether the solution does what the exercise asked."},{"id":"code_quality","label":"Code quality","guidance":"Readable, tested and well-structured code."},{"id":"design_choices","label":"Design choices","guidance":"The trade-offs they made and why they suit the problem."}]'),
            ('engineering-interview', 'Engineering interview', '[{"id":"role_skills","label":"Engineering skills the role needs","guidance":"The skills in the job description, shown in examples or answers."},{"id":"system_design","label":"System design","guidance":"How they would design a system like the ones this team builds."},{"id":"problem_solving","label":"Problem solving","guidance":"How they work through a problem taken from this job''s work."}]'),
            ('phone-screen', 'Phone screen', '[{"id":"role_requirements","label":"Meets the role''s requirements","guidance":"Licences, certificates or experience the job description asks for."},{"id":"relevant_experience","label":"Relevant experience","guidance":"Work they have done that is like this job''s tasks."}]'),
            ('hourly-interview', 'Hourly role interview', '[{"id":"role_skills","label":"Skills the role needs","guidance":"The tasks of the job, shown in examples or a short trial."},{"id":"safe_working","label":"Knows how to work safely","guidance":"The safety steps this job''s tasks need."},{"id":"relevant_experience","label":"Relevant experience","guidance":"Work they have done that is like this job''s tasks."}]');
            """;

        // Row-level security is forced on the template versions and jobs, and their policies match no row without a
        // tenant, so FORCE is off around the rewrite (AddSoftwareEngineerTemplateVersion2). The version triggers fire on
        // template and organization changes only, never on a definition. Each step:
        // - a v0.2 criterion becomes a 1 to 5 rating question with its id, label and guidance, optional as criteria were;
        // - an organization's template stage scorecard that is not a built-in's becomes one library scorecard per distinct
        //   (organization, stage name, criteria), named after its stage ("Screening scorecard");
        // - template stages, system versions included, then name their scorecard by id and hold no copy. Rewriting
        //   published versions in place changes how a scorecard is stored, not what it asks;
        // - a job's stage keeps its own copy, as questions, and names the scorecard it matches, if any.
        // The guard aborts the migration if any v0.2 scorecard is left or a system scorecard matched no built-in, because
        // the model can no longer read one.
        private const string Upgrade = BuiltIns + """

            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;

            CREATE FUNCTION pg_temp.scorecard_questions(criteria jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
                SELECT COALESCE(jsonb_agg(
                    jsonb_build_object('id', c ->> 'id', 'type', 'Rating5', 'label', c ->> 'label', 'required', false)
                    || jsonb_strip_nulls(jsonb_build_object('guidance', c ->> 'guidance'))
                    ORDER BY n), '[]'::jsonb)
                FROM jsonb_array_elements(criteria) WITH ORDINALITY AS e(c, n)
            $$;

            CREATE TEMP TABLE lifted_scorecards (id uuid PRIMARY KEY, organization_id uuid NOT NULL, stage_name text NOT NULL, criteria jsonb NOT NULL, name text NOT NULL);
            INSERT INTO lifted_scorecards (id, organization_id, stage_name, criteria, name)
            SELECT gen_random_uuid(), x.organization_id, x.stage_name, x.criteria,
                   left(x.stage_name, 66) || ' scorecard'
                   || CASE WHEN row_number() OVER (PARTITION BY x.organization_id, x.stage_name ORDER BY x.criteria::text) > 1
                           THEN ' ' || row_number() OVER (PARTITION BY x.organization_id, x.stage_name ORDER BY x.criteria::text) ELSE '' END
            FROM (
                SELECT DISTINCT v.organization_id, COALESCE(s.stage ->> 'name', 'Stage') AS stage_name, s.stage -> 'scorecard' -> 'criteria' AS criteria
                FROM hiring_template_versions v
                CROSS JOIN LATERAL jsonb_array_elements(v.definition -> 'workflow' -> 'stages') AS s(stage)
                WHERE v.organization_id IS NOT NULL
                  AND jsonb_typeof(s.stage -> 'scorecard' -> 'criteria') = 'array'
                  AND NOT EXISTS (SELECT FROM builtin_scorecards b WHERE b.criteria = s.stage -> 'scorecard' -> 'criteria')
            ) x;

            INSERT INTO scorecard_templates (id, organization_id, name, questions, copied_from, created_at, updated_at)
            SELECT id, organization_id, name, pg_temp.scorecard_questions(criteria), NULL, now(), now()
            FROM lifted_scorecards;

            CREATE FUNCTION pg_temp.scorecard_key(organization uuid, stage jsonb) RETURNS text LANGUAGE sql STABLE AS $$
                SELECT COALESCE(
                    (SELECT b.id FROM builtin_scorecards b WHERE b.criteria = stage -> 'scorecard' -> 'criteria'),
                    (SELECT l.id::text FROM lifted_scorecards l
                     WHERE l.organization_id = organization AND l.stage_name = COALESCE(stage ->> 'name', 'Stage') AND l.criteria = stage -> 'scorecard' -> 'criteria'))
            $$;

            CREATE FUNCTION pg_temp.scorecard_name(organization uuid, stage jsonb) RETURNS text LANGUAGE sql STABLE AS $$
                SELECT COALESCE(
                    (SELECT b.name FROM builtin_scorecards b WHERE b.criteria = stage -> 'scorecard' -> 'criteria'),
                    (SELECT l.name FROM lifted_scorecards l
                     WHERE l.organization_id = organization AND l.stage_name = COALESCE(stage ->> 'name', 'Stage') AND l.criteria = stage -> 'scorecard' -> 'criteria'),
                    left(COALESCE(stage ->> 'name', 'Stage'), 66) || ' scorecard')
            $$;

            CREATE FUNCTION pg_temp.template_stages(organization uuid, stages jsonb) RETURNS jsonb LANGUAGE sql STABLE AS $$
                SELECT COALESCE(jsonb_agg(
                    CASE WHEN jsonb_typeof(st -> 'scorecard' -> 'criteria') = 'array'
                         THEN (st - 'scorecard') || jsonb_build_object('scorecardTemplateId', pg_temp.scorecard_key(organization, st))
                         ELSE st END
                    ORDER BY n), '[]'::jsonb)
                FROM jsonb_array_elements(stages) WITH ORDINALITY AS e(st, n)
            $$;

            CREATE FUNCTION pg_temp.job_stages(organization uuid, stages jsonb) RETURNS jsonb LANGUAGE sql STABLE AS $$
                SELECT COALESCE(jsonb_agg(
                    CASE WHEN jsonb_typeof(st -> 'scorecard' -> 'criteria') = 'array'
                         THEN jsonb_set(st, '{scorecard}', jsonb_build_object(
                                  'name', pg_temp.scorecard_name(organization, st),
                                  'questions', pg_temp.scorecard_questions(st -> 'scorecard' -> 'criteria')))
                              || jsonb_strip_nulls(jsonb_build_object('scorecardTemplateId', pg_temp.scorecard_key(organization, st)))
                         ELSE st END
                    ORDER BY n), '[]'::jsonb)
                FROM jsonb_array_elements(stages) WITH ORDINALITY AS e(st, n)
            $$;

            UPDATE hiring_template_versions
            SET definition = jsonb_set(definition, '{workflow,stages}', pg_temp.template_stages(organization_id, definition -> 'workflow' -> 'stages'))
            WHERE jsonb_path_exists(definition, '$.workflow.stages[*].scorecard');

            UPDATE jobs
            SET workflow = jsonb_set(workflow, '{stages}', pg_temp.job_stages(organization_id, workflow -> 'stages'))
            WHERE jsonb_path_exists(workflow, '$.stages[*].scorecard.criteria');

            DO $$
            BEGIN
                IF EXISTS (SELECT FROM hiring_template_versions
                           WHERE jsonb_path_exists(definition, '$.workflow.stages[*].scorecard')
                              OR jsonb_path_exists(definition, '$.workflow.stages[*] ? (exists(@.scorecardTemplateId) && @.scorecardTemplateId == null)'))
                   OR EXISTS (SELECT FROM jobs WHERE jsonb_path_exists(workflow, '$.stages[*].scorecard.criteria')) THEN
                    RAISE EXCEPTION 'AddScorecardTemplates left a v0.2 scorecard behind, or found a system scorecard that is no built-in.';
                END IF;
            END
            $$;

            DROP FUNCTION pg_temp.job_stages(uuid, jsonb);
            DROP FUNCTION pg_temp.template_stages(uuid, jsonb);
            DROP FUNCTION pg_temp.scorecard_name(uuid, jsonb);
            DROP FUNCTION pg_temp.scorecard_key(uuid, jsonb);
            DROP FUNCTION pg_temp.scorecard_questions(jsonb);
            DROP TABLE lifted_scorecards;
            DROP TABLE builtin_scorecards;

            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;

            -- Row-level security like every tenant table (AddSignInAndTenantIsolation). A library scorecard is edited in
            -- place, so the application role keeps UPDATE.
            ALTER TABLE scorecard_templates ENABLE ROW LEVEL SECURITY;
            ALTER TABLE scorecard_templates FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON scorecard_templates
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // Back to v0.2: every question becomes a criterion with its id, label and guidance (answer kinds are lost, as v0.2
        // had none), a template stage gets its library scorecard's questions back inline, and a job's copy turns back into
        // criteria. A stage whose library scorecard is gone loses its scorecard. Submitted v0.3 answers and review stages
        // go with their columns.
        private const string Downgrade = BuiltIns + """

            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE scorecard_templates NO FORCE ROW LEVEL SECURITY;

            CREATE FUNCTION pg_temp.scorecard_criteria(questions jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
                SELECT COALESCE(jsonb_agg(
                    jsonb_strip_nulls(jsonb_build_object('id', q ->> 'id', 'label', q ->> 'label', 'guidance', q ->> 'guidance'))
                    ORDER BY n), '[]'::jsonb)
                FROM jsonb_array_elements(questions) WITH ORDINALITY AS e(q, n)
            $$;

            CREATE FUNCTION pg_temp.library_criteria(organization uuid, key text) RETURNS jsonb LANGUAGE sql STABLE AS $$
                SELECT COALESCE(
                    (SELECT b.criteria FROM builtin_scorecards b WHERE b.id = key),
                    (SELECT pg_temp.scorecard_criteria(t.questions) FROM scorecard_templates t
                     WHERE t.organization_id = organization AND t.id::text = key))
            $$;

            CREATE FUNCTION pg_temp.v02_template_stages(organization uuid, stages jsonb) RETURNS jsonb LANGUAGE sql STABLE AS $$
                SELECT COALESCE(jsonb_agg(
                    CASE WHEN pg_temp.library_criteria(organization, st ->> 'scorecardTemplateId') IS NOT NULL
                         THEN (st - 'scorecardTemplateId') || jsonb_build_object('scorecard', jsonb_build_object('criteria',
                                  pg_temp.library_criteria(organization, st ->> 'scorecardTemplateId')))
                         ELSE st - 'scorecardTemplateId' END
                    ORDER BY n), '[]'::jsonb)
                FROM jsonb_array_elements(stages) WITH ORDINALITY AS e(st, n)
            $$;

            CREATE FUNCTION pg_temp.v02_job_stages(stages jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
                SELECT COALESCE(jsonb_agg(
                    CASE WHEN jsonb_typeof(st -> 'scorecard' -> 'questions') = 'array'
                         THEN jsonb_set(st - 'scorecardTemplateId', '{scorecard}',
                                  jsonb_build_object('criteria', pg_temp.scorecard_criteria(st -> 'scorecard' -> 'questions')))
                         ELSE st - 'scorecardTemplateId' END
                    ORDER BY n), '[]'::jsonb)
                FROM jsonb_array_elements(stages) WITH ORDINALITY AS e(st, n)
            $$;

            UPDATE hiring_template_versions
            SET definition = jsonb_set(definition, '{workflow,stages}', pg_temp.v02_template_stages(organization_id, definition -> 'workflow' -> 'stages'))
            WHERE jsonb_path_exists(definition, '$.workflow.stages[*].scorecardTemplateId');

            UPDATE jobs
            SET workflow = jsonb_set(workflow, '{stages}', pg_temp.v02_job_stages(workflow -> 'stages'))
            WHERE jsonb_path_exists(workflow, '$.stages[*].scorecard') OR jsonb_path_exists(workflow, '$.stages[*].scorecardTemplateId');

            DROP FUNCTION pg_temp.v02_job_stages(jsonb);
            DROP FUNCTION pg_temp.v02_template_stages(uuid, jsonb);
            DROP FUNCTION pg_temp.library_criteria(uuid, text);
            DROP FUNCTION pg_temp.scorecard_criteria(jsonb);
            DROP TABLE builtin_scorecards;

            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stage_id",
                table: "application_tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "answers",
                table: "application_scorecards",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "scorecard_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    questions = table.Column<string>(type: "jsonb", nullable: false),
                    copied_from = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scorecard_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_scorecard_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scorecard_templates_organization_id_name",
                table: "scorecard_templates",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.Sql(Upgrade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Downgrade);

            migrationBuilder.DropTable(
                name: "scorecard_templates");

            migrationBuilder.DropColumn(
                name: "stage_id",
                table: "application_tasks");

            migrationBuilder.DropColumn(
                name: "answers",
                table: "application_scorecards");
        }
    }
}
