using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Email bodies become rich text, the sanitized HTML job descriptions already were (docs/architecture/rich-text.md),
    /// so every body converts from plain text: a paragraph per blank-line block, a line break per single newline, and
    /// lines that all start with "- " as a list, as the dashboard's text box read them. A job description stored as plain
    /// text (an API client sent no tags) converts the same way, and so does its published snapshot, so equal content
    /// stays equal and no job shows unpublished changes it does not have. The words do not change, only their markup,
    /// so the raw updates go around change history on purpose (docs/architecture/change-history.md).
    /// </summary>
    public partial class ConvertPlainTextToRichText : Migration
    {
        /// <summary>
        /// The conversion, as a function for this session only. <c>escape</c> encodes the text first: an email body is
        /// plain text, while a description is already sanitized HTML text with its entities in place. Quotes stay as they
        /// are, so Liquid's string literals read the same. The same rule as <c>RichText.Sanitize</c> for loose text.
        /// </summary>
        public const string PlainTextToHtml = """
            CREATE FUNCTION pg_temp.plain_text_to_html(body text, escape boolean) RETURNS text
            LANGUAGE plpgsql IMMUTABLE AS $fn$
            DECLARE
                block text;
                lines text[];
                html text := '';
                items text := '';
            BEGIN
                body := replace(replace(body, E'\r\n', E'\n'), E'\r', E'\n');
                IF escape THEN
                    body := replace(replace(replace(replace(body, '&', '&amp;'), '<', '&lt;'), '>', '&gt;'), chr(160), '&nbsp;');
                END IF;

                FOREACH block IN ARRAY regexp_split_to_array(body, E'\n[ \t]*\n') LOOP
                    SELECT coalesce(array_agg(line ORDER BY n), '{}') INTO lines
                    FROM (
                        SELECT regexp_replace(btrim(raw, E' \t'), E'[ \t]+', ' ', 'g') AS line, n
                        FROM unnest(string_to_array(block, E'\n')) WITH ORDINALITY AS l(raw, n)
                    ) AS t
                    WHERE line <> '';
                    CONTINUE WHEN cardinality(lines) = 0;

                    IF NOT EXISTS (SELECT 1 FROM unnest(lines) AS l(line) WHERE left(line, 2) <> '- ') THEN
                        items := items || (
                            SELECT string_agg('<li>' || btrim(substr(line, 3), E' \t') || '</li>', '' ORDER BY n)
                            FROM unnest(lines) WITH ORDINALITY AS l(line, n));
                    ELSE
                        IF items <> '' THEN
                            html := html || '<ul>' || items || '</ul>';
                            items := '';
                        END IF;

                        html := html || '<p>' || array_to_string(lines, '<br>') || '</p>';
                    END IF;
                END LOOP;

                IF items <> '' THEN
                    html := html || '<ul>' || items || '</ul>';
                END IF;

                RETURN html;
            END
            $fn$;
            """;

        /// <summary>
        /// The updates. FORCE binds the owner running this too, and with no <c>app.tenant_id</c> every row would be out of
        /// sight, so it is lifted around each update, as in <c>GrantCreateTasksToCustomRoles</c>.
        /// </summary>
        public const string Convert = """
            ALTER TABLE email_templates NO FORCE ROW LEVEL SECURITY;
            UPDATE email_templates SET body = pg_temp.plain_text_to_html(body, true);
            ALTER TABLE email_templates FORCE ROW LEVEL SECURITY;

            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
            UPDATE jobs SET description = pg_temp.plain_text_to_html(description, false)
            WHERE description <> '' AND description !~ '<';
            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;

            ALTER TABLE job_versions NO FORCE ROW LEVEL SECURITY;
            UPDATE job_versions
            SET snapshot = jsonb_set(snapshot, '{description}', to_jsonb(pg_temp.plain_text_to_html(snapshot->>'description', false)))
            WHERE snapshot->>'description' <> '' AND snapshot->>'description' !~ '<';
            ALTER TABLE job_versions FORCE ROW LEVEL SECURITY;

            DROP FUNCTION pg_temp.plain_text_to_html(text, boolean);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wider first: HTML is longer than the text it replaces.
            migrationBuilder.AlterColumn<string>(
                name: "body",
                table: "email_templates",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000);

            migrationBuilder.Sql(PlainTextToHtml);
            migrationBuilder.Sql(Convert);
        }

        /// <summary>
        /// Bodies stay HTML, which code from before this migration shows as text, and narrowing fails while any body is
        /// longer than the old limit. Nothing is hosted, so going back is a local rollback only.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "body",
                table: "email_templates",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20000)",
                oldMaxLength: 20000);
        }
    }
}
