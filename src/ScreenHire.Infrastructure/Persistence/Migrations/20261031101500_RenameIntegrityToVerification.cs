using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Integrity becomes Verification (ADR-0019). Written by hand: the entity types and the tables both change name, so
    /// EF would drop and create the tables. This renames them, their keys and indexes, and rewrites the names stored as
    /// data, and never drops anything. Row-level security policies and grants belong to a table and follow its rename.
    /// FORCE binds the owner running this too, so it is lifted around each rewrite, as in GrantCreateTasksToCustomRoles.
    /// People's words (tags, automation names and ids, notes, change-history lines) and webhook deliveries, the exact
    /// bytes that were sent, keep what they say.
    /// </summary>
    public partial class RenameIntegrityToVerification : Migration
    {
        private static readonly (string Old, string New)[] Tables =
        [
            ("integrity_analyzer_runs", "verification_analyzer_runs"),
            ("integrity_signals", "verification_signals"),
            ("integrity_assessments", "verification_assessments"),
            ("integrity_review_actions", "verification_review_actions"),
            ("integrity_settings", "verification_settings"),
        ];

        // Primary, alternate and foreign keys, by their new table. EF truncates names to 63 characters, so the new names
        // are the ones the model snapshot gives, not the old ones with a word swapped.
        private static readonly (string Table, string Old, string New)[] Constraints =
        [
            ("verification_analyzer_runs", "pk_integrity_analyzer_runs", "pk_verification_analyzer_runs"),
            ("verification_signals", "pk_integrity_signals", "pk_verification_signals"),
            ("verification_assessments", "pk_integrity_assessments", "pk_verification_assessments"),
            ("verification_review_actions", "pk_integrity_review_actions", "pk_verification_review_actions"),
            ("verification_settings", "pk_integrity_settings", "pk_verification_settings"),
            ("verification_analyzer_runs", "ak_integrity_analyzer_runs_organization_id_id", "ak_verification_analyzer_runs_organization_id_id"),
            ("verification_assessments", "ak_integrity_assessments_organization_id_id", "ak_verification_assessments_organization_id_id"),
            ("verification_analyzer_runs", "fk_integrity_analyzer_runs_applications_organization_id_applic", "fk_verification_analyzer_runs_applications_organization_id_app"),
            ("verification_assessments", "fk_integrity_assessments_applications_organization_id_applicat", "fk_verification_assessments_applications_organization_id_appli"),
            ("verification_review_actions", "fk_integrity_review_actions_applications_organization_id_appli", "fk_verification_review_actions_applications_organization_id_ap"),
            ("verification_review_actions", "fk_integrity_review_actions_integrity_assessments_organization", "fk_verification_review_actions_verification_assessments_organi"),
            ("verification_settings", "fk_integrity_settings_organizations_organization_id", "fk_verification_settings_organizations_organization_id"),
            ("verification_signals", "fk_integrity_signals_applications_organization_id_application_", "fk_verification_signals_applications_organization_id_applicati"),
            ("verification_signals", "fk_integrity_signals_integrity_analyzer_runs_organization_id_a", "fk_verification_signals_verification_analyzer_runs_organizatio"),
        ];

        private static readonly (string Table, string Old, string New)[] Indexes =
        [
            ("verification_analyzer_runs", "ix_integrity_analyzer_runs_organization_id_application_id_anal", "ix_verification_analyzer_runs_organization_id_application_id_a"),
            ("verification_assessments", "ix_integrity_assessments_organization_id_application_id_kind_a", "ix_verification_assessments_organization_id_application_id_kin"),
            ("verification_assessments", "ix_integrity_assessments_organization_id_application_id_kind_f", "ix_verification_assessments_organization_id_application_id_kin1"),
            ("verification_review_actions", "ix_integrity_review_actions_one_dismissal", "ix_verification_review_actions_one_dismissal"),
            ("verification_review_actions", "ix_integrity_review_actions_organization_id_application_id_occ", "ix_verification_review_actions_organization_id_application_id_"),
            ("verification_review_actions", "ix_integrity_review_actions_organization_id_assessment_id", "ix_verification_review_actions_organization_id_assessment_id"),
            ("verification_signals", "ix_integrity_signals_organization_id_analyzer_run_id", "ix_verification_signals_organization_id_analyzer_run_id"),
            ("verification_signals", "ix_integrity_signals_organization_id_application_id", "ix_verification_signals_organization_id_application_id"),
        ];

        // Names stored inside JSON, as whole quoted strings: the workflow's policy key, the trigger, the two facts (in
        // definitions and in an automation run's rule results) and the facts' keys in an automation run's snapshot.
        private static readonly (string Old, string New)[] JsonTokens =
        [
            ("\"integrityPolicy\"", "\"verificationPolicy\""),
            ("\"IntegrityAssessed\"", "\"VerificationAssessed\""),
            ("\"integrity.authenticity\"", "\"verification.authenticity\""),
            ("\"integrity.aiAssistance\"", "\"verification.aiAssistance\""),
            ("\"integrityAuthenticity\"", "\"verificationAuthenticity\""),
            ("\"integrityAiAssistance\"", "\"verificationAiAssistance\""),
        ];

        private static readonly (string Table, string Column)[] JsonColumns =
        [
            ("hiring_template_versions", "definition"),
            ("jobs", "workflow"),
            ("automation_runs", "facts"),
            ("automation_runs", "rule_results"),
        ];

        // Where version 4 keeps its verification automation (the second), its name and its tag (its second action).
        private const string IdPath = "{workflow,automations,1,id}";
        private const string NamePath = "{workflow,automations,1,name}";
        private const string TagPath = "{workflow,automations,1,actions,1,tag}";

        private const string SoftwareEngineer = "0199e8a0-0000-7000-8000-000000000001";
        private const string SoftwareEngineerVersion4 = "0199e8a0-0000-7000-8000-000000000104";
        private const string SoftwareEngineerVersion5 = "0199e8a0-0000-7000-8000-000000000105";

        private static readonly string[] RewrittenTables =
        [
            "hiring_template_versions", "hiring_templates", "jobs", "automation_runs", "application_events", "custom_roles",
            "webhook_endpoints", "change_records",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Renames(Tables, Constraints, Indexes, toNew: true));
            migrationBuilder.Sql(Rewrite(toNew: true));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Rewrite(toNew: false));
            migrationBuilder.Sql(Renames(Tables, Constraints, Indexes, toNew: false));
        }

        private static string Renames(
            (string Old, string New)[] tables,
            (string Table, string Old, string New)[] constraints,
            (string Table, string Old, string New)[] indexes,
            bool toNew)
        {
            var tableSql = tables.Select(t => toNew
                ? $"ALTER TABLE {t.Old} RENAME TO {t.New};"
                : $"ALTER TABLE {t.New} RENAME TO {t.Old};");
            // Up renames the tables first and then their objects; Down renames the objects while the tables still have
            // their new names, then the tables.
            var constraintSql = constraints.Select(c => toNew
                ? $"ALTER TABLE {c.Table} RENAME CONSTRAINT {c.Old} TO {c.New};"
                : $"ALTER TABLE {c.Table} RENAME CONSTRAINT {c.New} TO {c.Old};");
            var indexSql = indexes.Select(i => toNew
                ? $"ALTER INDEX {i.Old} RENAME TO {i.New};"
                : $"ALTER INDEX {i.New} RENAME TO {i.Old};");
            return string.Join('\n', toNew
                ? tableSql.Concat(constraintSql).Concat(indexSql)
                : constraintSql.Concat(indexSql).Concat(tableSql));
        }

        private static string Rewrite(bool toNew)
        {
            string From((string Old, string New) pair) => toNew ? pair.Old : pair.New;
            string To((string Old, string New) pair) => toNew ? pair.New : pair.Old;
            string Text(string value) => value.Replace("'", "''", System.StringComparison.Ordinal);

            var sql = new System.Text.StringBuilder();
            foreach (var table in RewrittenTables)
                sql.AppendLine($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");

            if (!toNew)
            {
                // Version 5 says verification; the code before this migration reads only what version 4 says.
                sql.AppendLine($"DELETE FROM hiring_template_versions WHERE id = '{SoftwareEngineerVersion5}';");
                sql.AppendLine($"UPDATE hiring_templates SET latest_published_version = 4 WHERE id = '{SoftwareEngineer}' AND latest_published_version = 5;");
            }

            foreach (var (table, column) in JsonColumns)
            {
                var expression = $"{column}::text";
                foreach (var token in JsonTokens)
                    expression = $"replace({expression}, '{Text(From(token))}', '{Text(To(token))}')";
                var any = string.Join(" OR ", JsonTokens.Select(t => $"{column}::text LIKE '%{Text(From(t))}%'"));
                sql.AppendLine($"UPDATE {table} SET {column} = ({expression})::jsonb WHERE {any};");
            }

            (string Old, string New) trigger = ("IntegrityAssessed", "VerificationAssessed");
            (string Old, string New) permission = ("ReviewIntegrity", "ReviewVerification");
            (string Old, string New) webhookEvent = ("integrity.assessed", "verification.assessed");
            (string Old, string New) subject = ("IntegritySettings", "VerificationSettings");
            sql.AppendLine($"UPDATE automation_runs SET trigger = '{To(trigger)}' WHERE trigger = '{From(trigger)}';");
            sql.AppendLine($"UPDATE application_events SET type = '{To(trigger)}' WHERE type = '{From(trigger)}';");
            sql.AppendLine($"UPDATE custom_roles SET permissions = array_replace(permissions, '{From(permission)}', '{To(permission)}') WHERE '{From(permission)}' = ANY (permissions);");
            sql.AppendLine($"UPDATE webhook_endpoints SET events = array_replace(events, '{From(webhookEvent)}', '{To(webhookEvent)}') WHERE '{From(webhookEvent)}' = ANY (events);");
            sql.AppendLine($"UPDATE change_records SET subject_kind = '{To(subject)}' WHERE subject_kind = '{From(subject)}';");

            if (toNew)
            {
                // A change to a system template's content is a new version (SystemTemplates): version 4 with its
                // verification automation's name and tag saying verification. Its id stays integrity_review, because
                // automation ids feed idempotency keys. The WHERE pins version 4 as expected, so the insert does nothing
                // otherwise.
                sql.AppendLine($"""
                    INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
                    SELECT '{SoftwareEngineerVersion5}', NULL, v.template_id, 5, 'Published',
                           jsonb_set(jsonb_set(v.definition,
                               '{NamePath}', '"Ask a reviewer to look at verification findings"'::jsonb),
                               '{TagPath}', '"verification-review"'::jsonb),
                           '2026-10-31T10:15:00Z', '2026-10-31T10:15:00Z'
                    FROM hiring_template_versions v
                    WHERE v.id = '{SoftwareEngineerVersion4}'
                      AND v.definition #>> '{IdPath}' = 'integrity_review'
                      AND v.definition #>> '{TagPath}' = 'integrity-review';
                    UPDATE hiring_templates SET latest_published_version = 5
                    WHERE id = '{SoftwareEngineer}' AND latest_published_version < 5
                      AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '{SoftwareEngineerVersion5}');
                    """);
            }

            // Stop here rather than leave a name the code can no longer read.
            var leftovers = string.Join(" UNION ALL ", JsonColumns.Select(c =>
                    $"SELECT 1 FROM {c.Table} WHERE " + string.Join(" OR ", JsonTokens.Select(t => $"{c.Column}::text LIKE '%{Text(From(t))}%'")))
                .Append($"SELECT 1 FROM automation_runs WHERE trigger = '{From(trigger)}'")
                .Append($"SELECT 1 FROM application_events WHERE type = '{From(trigger)}'")
                .Append($"SELECT 1 FROM custom_roles WHERE '{From(permission)}' = ANY (permissions)")
                .Append($"SELECT 1 FROM webhook_endpoints WHERE '{From(webhookEvent)}' = ANY (events)")
                .Append($"SELECT 1 FROM change_records WHERE subject_kind = '{From(subject)}'"));
            sql.AppendLine($"""
                DO $$
                BEGIN
                    IF EXISTS ({leftovers}) THEN
                        RAISE EXCEPTION 'RenameIntegrityToVerification left a stored name unrewritten';
                    END IF;
                END $$;
                """);

            foreach (var table in RewrittenTables.Reverse())
                sql.AppendLine($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
            return sql.ToString();
        }
    }
}
