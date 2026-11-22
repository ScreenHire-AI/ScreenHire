using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The built-in templates' automations get short names, because the Pipeline tab lists automations by their own names
    /// (design system section 11, Stage chips): "Queue for review if no right to work" is "Right-to-work check", "Ask a
    /// reviewer to look at verification findings" is "Verification review" and "Tell the team about new applications" is
    /// "New application alert". Each system template gets a new version that is its newest with those names changed, by
    /// automation id, and nothing else. A published version never changes: a job already on an earlier version keeps it
    /// and sees "update available" (docs/architecture/workflow-engine.md). Each INSERT does nothing if the source version is
    /// missing or holds no automations.
    /// </summary>
    public partial class ShortenSystemAutomationNames : Migration
    {
        // (template id, source version id, new version id, new version number), each newest version as of this migration.
        private static readonly (string Template, string From, string To, int Version)[] Versions =
        [
            ("0199e8a0-0000-7000-8000-000000000001", "0199e8a0-0000-7000-8000-000000000105", "0199e8a0-0000-7000-8000-000000000106", 6), // Software engineer
            ("0199e8a0-0000-7000-8000-000000000002", "0199e8a0-0000-7000-8000-000000000203", "0199e8a0-0000-7000-8000-000000000204", 4), // Hourly role
            ("0199e8a0-0000-7000-8000-000000000003", "0199e8a0-0000-7000-8000-000000000301", "0199e8a0-0000-7000-8000-000000000302", 2), // Office role
            ("0199e8a0-0000-7000-8000-000000000004", "0199e8a0-0000-7000-8000-000000000403", "0199e8a0-0000-7000-8000-000000000404", 4), // Trade role with a trial shift
            ("0199e8a0-0000-7000-8000-000000000005", "0199e8a0-0000-7000-8000-000000000501", "0199e8a0-0000-7000-8000-000000000502", 2), // High volume
            ("0199e8a0-0000-7000-8000-000000000006", "0199e8a0-0000-7000-8000-000000000602", "0199e8a0-0000-7000-8000-000000000603", 3), // Hospitality
        ];

        // The automation's id, then its new name. Ids are the templates' own, the same in every version.
        private const string Renamed = """
            CASE a ->> 'id'
                WHEN 'right_to_work_review' THEN a || '{"name":"Right-to-work check"}'::jsonb
                WHEN 'integrity_review' THEN a || '{"name":"Verification review"}'::jsonb
                WHEN 'notify_team' THEN a || '{"name":"New application alert"}'::jsonb
                ELSE a
            END
            """;

        // Row-level security is forced on these tables and its policies let nobody, the owner included, write a row
        // without an organization, so FORCE is off around the writes (AddStartingPointTemplates' note on system templates).
        private const string NoForce = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            """;

        private const string Force = """
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private static string Insert((string Template, string From, string To, int Version) row) => $"""
            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '{row.To}', NULL, v.template_id, {row.Version}, 'Published',
                   jsonb_set(
                       v.definition,
                       '{"{"}workflow,automations{"}"}',
                       (SELECT jsonb_agg({Renamed} ORDER BY ord)
                        FROM jsonb_array_elements(v.definition #> '{"{"}workflow,automations{"}"}') WITH ORDINALITY AS t(a, ord))),
                   '2026-11-22T16:00:00Z', '2026-11-22T16:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '{row.From}'
              AND jsonb_array_length(COALESCE(v.definition #> '{"{"}workflow,automations{"}"}', '[]'::jsonb)) > 0;

            UPDATE hiring_templates SET latest_published_version = {row.Version}
            WHERE id = '{row.Template}' AND latest_published_version < {row.Version}
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '{row.To}');

            """;

        private static string Delete((string Template, string From, string To, int Version) row) => $"""
            UPDATE hiring_templates SET latest_published_version = {row.Version - 1} WHERE id = '{row.Template}';
            DELETE FROM hiring_template_versions WHERE id = '{row.To}';

            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(NoForce + string.Concat(Versions.Select(Insert)) + Force);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            // Jobs that used these versions keep their copied workflow; their source is set to NULL by the foreign key.
            migrationBuilder.Sql(NoForce + string.Concat(Versions.Select(Delete)) + Force);
    }
}
