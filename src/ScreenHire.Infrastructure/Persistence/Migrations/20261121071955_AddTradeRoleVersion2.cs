using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Version 2 of the "Trade role with a trial shift" system template: version 1 with the trial shift stage naming its
    /// meeting "trial shift" (<c>meetingName</c>), so the invitation, the reminder and the booking page say trial shift
    /// where they said interview. Version 1 stays as it was: a published version never changes, and jobs copied from it
    /// see "update available" (docs/architecture/workflow-engine.md).
    /// </summary>
    public partial class AddTradeRoleVersion2 : Migration
    {
        // Row-level security is forced on these tables and its policies let nobody, the owner included, write a row
        // without an organization, so FORCE is off around the insert (AddHiringModel's note on system templates). The
        // WHERE clause pins the stage being changed, so the insert does nothing if version 1 is not as expected.
        private const string InsertVersion2 = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000402', NULL, v.template_id, 2, 'Published',
                   jsonb_set(
                       v.definition,
                       '{workflow,stages}',
                       (SELECT jsonb_agg(CASE WHEN s ->> 'id' = 'trial_shift' THEN s || '{"meetingName":"trial shift"}'::jsonb ELSE s END ORDER BY ord)
                        FROM jsonb_array_elements(v.definition #> '{workflow,stages}') WITH ORDINALITY AS t(s, ord))),
                   '2026-11-21T08:00:00Z', '2026-11-21T08:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000401'
              AND EXISTS (SELECT FROM jsonb_array_elements(v.definition #> '{workflow,stages}') s WHERE s ->> 'id' = 'trial_shift');

            UPDATE hiring_templates SET latest_published_version = 2
            WHERE id = '0199e8a0-0000-7000-8000-000000000004'
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000402');

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private const string DeleteVersion2 = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            -- Jobs that used version 2 keep their copied workflow; their source is set to NULL by the foreign key.
            DELETE FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000402';
            UPDATE hiring_templates SET latest_published_version = 1 WHERE id = '0199e8a0-0000-7000-8000-000000000004';
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InsertVersion2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeleteVersion2);
        }
    }
}
