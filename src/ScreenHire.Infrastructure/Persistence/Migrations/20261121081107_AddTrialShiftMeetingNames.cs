using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The trial shift starting points name their meeting. "Trade role with a trial shift" version 3 is version 2 and
    /// "Hospitality" version 2 is version 1, each with its <c>trial_shift</c> stage's <c>meetingName</c> set to
    /// "trial shift", so the invitation, the reminder and the booking page say trial shift where they said interview.
    /// A published version never changes: a job already on an earlier version keeps it and sees "update available"
    /// (docs/architecture/workflow-engine.md). Each INSERT does nothing if the earlier version has no such stage.
    /// </summary>
    public partial class AddTrialShiftMeetingNames : Migration
    {
        // Row-level security is forced on these tables and its policies let nobody, the owner included, write a row
        // without an organization, so FORCE is off around the inserts (AddStartingPointTemplates' note on system templates).
        private const string InsertVersions = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000403', NULL, v.template_id, 3, 'Published',
                   jsonb_set(
                       v.definition,
                       '{workflow,stages}',
                       (SELECT jsonb_agg(CASE WHEN s ->> 'id' = 'trial_shift' THEN s || '{"meetingName":"trial shift"}'::jsonb ELSE s END ORDER BY ord)
                        FROM jsonb_array_elements(v.definition #> '{workflow,stages}') WITH ORDINALITY AS t(s, ord))),
                   '2026-11-21T08:00:00Z', '2026-11-21T08:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000402'
              AND EXISTS (SELECT FROM jsonb_array_elements(v.definition #> '{workflow,stages}') s WHERE s ->> 'id' = 'trial_shift');

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000602', NULL, v.template_id, 2, 'Published',
                   jsonb_set(
                       v.definition,
                       '{workflow,stages}',
                       (SELECT jsonb_agg(CASE WHEN s ->> 'id' = 'trial_shift' THEN s || '{"meetingName":"trial shift"}'::jsonb ELSE s END ORDER BY ord)
                        FROM jsonb_array_elements(v.definition #> '{workflow,stages}') WITH ORDINALITY AS t(s, ord))),
                   '2026-11-21T08:00:00Z', '2026-11-21T08:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000601'
              AND EXISTS (SELECT FROM jsonb_array_elements(v.definition #> '{workflow,stages}') s WHERE s ->> 'id' = 'trial_shift');

            UPDATE hiring_templates SET latest_published_version = 3
            WHERE id = '0199e8a0-0000-7000-8000-000000000004' AND latest_published_version < 3
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000403');

            UPDATE hiring_templates SET latest_published_version = 2
            WHERE id = '0199e8a0-0000-7000-8000-000000000006' AND latest_published_version < 2
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000602');

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private const string DeleteVersions = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            -- Jobs that used these versions keep their copied workflow; their source is set to NULL by the foreign key.
            UPDATE hiring_templates SET latest_published_version = 2 WHERE id = '0199e8a0-0000-7000-8000-000000000004';
            UPDATE hiring_templates SET latest_published_version = 1 WHERE id = '0199e8a0-0000-7000-8000-000000000006';
            DELETE FROM hiring_template_versions WHERE id IN ('0199e8a0-0000-7000-8000-000000000403', '0199e8a0-0000-7000-8000-000000000602');
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(InsertVersions);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DeleteVersions);
    }
}
