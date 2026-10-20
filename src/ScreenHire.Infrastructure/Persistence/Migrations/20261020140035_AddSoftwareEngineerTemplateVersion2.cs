using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Version 2 of the "Software engineer" system template. Version 1 (AddHiringModel) asks a reviewer to look at
    /// integrity findings when <c>integrity.aiAssistance</c> equals <c>ReviewSuggested</c>, which is an authenticity
    /// level, so that half never matched. Version 2 is version 1 with only that automation's conditions corrected: either
    /// assessment above its lowest level (docs/architecture/integrity-engine.md §10.3). Version 1 stays as it was: a
    /// published version never changes, and jobs copied from it see "update available".
    /// </summary>
    public partial class AddSoftwareEngineerTemplateVersion2 : Migration
    {
        // Row-level security is forced on these tables and its policies let nobody, the owner included, write a row
        // without an organization, so FORCE is off around the insert (AddHiringModel's note on system templates). The
        // WHERE clause pins the automation being changed, so the insert does nothing if version 1 is not as expected.
        private const string InsertVersion2 = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000102', NULL, v.template_id, 2, 'Published',
                   jsonb_set(v.definition, '{workflow,automations,1,conditions}', '{"match":"Any","rules":[{"fact":"integrity.authenticity","operator":"In","values":["ReviewSuggested","VerificationRecommended"],"negate":false},{"fact":"integrity.aiAssistance","operator":"In","values":["SomeIndicators","StrongIndicators"],"negate":false}]}'::jsonb),
                   '2026-10-20T14:00:00Z', '2026-10-20T14:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000101'
              AND v.definition #>> '{workflow,automations,1,id}' = 'integrity_review';

            UPDATE hiring_templates SET latest_published_version = 2
            WHERE id = '0199e8a0-0000-7000-8000-000000000001'
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000102');

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private const string DeleteVersion2 = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            -- Jobs that used version 2 keep their copied workflow; their source is set to NULL by the foreign key.
            DELETE FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000102';
            UPDATE hiring_templates SET latest_published_version = 1 WHERE id = '0199e8a0-0000-7000-8000-000000000001';
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
