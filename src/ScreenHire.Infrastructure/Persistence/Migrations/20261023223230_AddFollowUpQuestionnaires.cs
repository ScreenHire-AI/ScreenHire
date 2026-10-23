using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Follow-up questionnaires (workflow-engine.md §8): the <c>questionnaire_requests</c> table, the trigger's questionnaire
    /// on automation runs, the Data Protection key ring the API and the worker share, and the system templates' new
    /// versions whose right-to-work automation fires on the application ("Software engineer" version 4, "Hourly role"
    /// version 3).
    /// </summary>
    public partial class AddFollowUpQuestionnaires : Migration
    {
        // Row-level security like every tenant table (AddSignInAndTenantIsolation). screenhire_app keeps its default
        // privileges: the worker creates and resends requests, the API records the answers.
        //
        // data_protection_keys has no organization and no row-level security on purpose: it is the one key ring that
        // protects every organization's cookies, antiforgery tokens and questionnaire links, read and written by the API
        // and the worker as screenhire_app (DataProtectionRegistration). A key is no tenant's data.
        //
        // System template versions: the right-to-work automation (automations[0] in both templates) fired on
        // QuestionnaireCompleted, which nothing raised; it checks an apply-form answer, so it now fires on
        // ApplicationSubmitted. FORCE is off around the inserts as in AddScorecards, and each WHERE clause pins the
        // automation being changed, so an insert does nothing if the version it copies is not as expected. The earlier
        // versions stay as they were, and jobs copied from them see "update available".
        private const string EnableRowLevelSecurityAndTemplates = """
            ALTER TABLE questionnaire_requests ENABLE ROW LEVEL SECURITY;
            ALTER TABLE questionnaire_requests FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON questionnaire_requests
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000104', NULL, v.template_id, 4, 'Published',
                   jsonb_set(v.definition, '{workflow,automations,0,trigger}', '{"type":"ApplicationSubmitted"}'::jsonb),
                   '2026-10-23T22:00:00Z', '2026-10-23T22:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000103'
              AND v.definition #>> '{workflow,automations,0,id}' = 'right_to_work_review'
              AND v.definition #>> '{workflow,automations,0,trigger,type}' = 'QuestionnaireCompleted';

            UPDATE hiring_templates SET latest_published_version = 4
            WHERE id = '0199e8a0-0000-7000-8000-000000000001' AND latest_published_version < 4
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000104');

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000203', NULL, v.template_id, 3, 'Published',
                   jsonb_set(v.definition, '{workflow,automations,0,trigger}', '{"type":"ApplicationSubmitted"}'::jsonb),
                   '2026-10-23T22:00:00Z', '2026-10-23T22:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000202'
              AND v.definition #>> '{workflow,automations,0,id}' = 'right_to_work_review'
              AND v.definition #>> '{workflow,automations,0,trigger,type}' = 'QuestionnaireCompleted';

            UPDATE hiring_templates SET latest_published_version = 3
            WHERE id = '0199e8a0-0000-7000-8000-000000000002' AND latest_published_version < 3
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000203');

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private const string DisableRowLevelSecurityAndTemplates = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            -- Jobs that used these versions keep their copied workflow; their source is set to NULL by the foreign key.
            DELETE FROM hiring_template_versions WHERE id IN ('0199e8a0-0000-7000-8000-000000000104', '0199e8a0-0000-7000-8000-000000000203');
            UPDATE hiring_templates SET latest_published_version = 3 WHERE id = '0199e8a0-0000-7000-8000-000000000001';
            UPDATE hiring_templates SET latest_published_version = 2 WHERE id = '0199e8a0-0000-7000-8000-000000000002';
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS tenant_isolation ON questionnaire_requests;
            ALTER TABLE questionnaire_requests NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE questionnaire_requests DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trigger_questionnaire_id",
                table: "automation_runs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    questionnaire_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    first_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    times_sent = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    questions = table.Column<string>(type: "jsonb", nullable: false),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_requests_applications_organization_id_applica",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_questionnaire_requests_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_requests_organization_id_application_id_quest",
                table: "questionnaire_requests",
                columns: new[] { "organization_id", "application_id", "questionnaire_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_requests_organization_id_job_id",
                table: "questionnaire_requests",
                columns: new[] { "organization_id", "job_id" });

            migrationBuilder.Sql(EnableRowLevelSecurityAndTemplates);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurityAndTemplates);

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "questionnaire_requests");

            migrationBuilder.DropColumn(
                name: "trigger_questionnaire_id",
                table: "automation_runs");
        }
    }
}
