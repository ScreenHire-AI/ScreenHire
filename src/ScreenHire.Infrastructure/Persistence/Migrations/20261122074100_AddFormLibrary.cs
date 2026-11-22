using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The form library (docs/architecture/workflow-engine.md section 7, Forms): <c>form_templates</c> and their numbered
    /// <c>form_template_versions</c>, versioned like hiring templates, and on <c>questionnaire_requests</c> the form's kind
    /// and library form, copied when it is sent. Existing follow-up questionnaires become library forms with their names
    /// and questions intact: each own template's current versions (the newest published one and the draft) link each of
    /// their follow-ups to one, and a job's own follow-up that no template version holds becomes one too.
    /// </summary>
    public partial class AddFormLibrary : Migration
    {
        // Tenant row-level security like every tenant table. A published version is immutable in the database too, as a
        // hiring template version is (AddHiringModel): the update and delete policies match drafts only. Deleting a form
        // would cascade to its published versions, which are meant to be permanent, so the application role cannot.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE form_templates ENABLE ROW LEVEL SECURITY;
            ALTER TABLE form_templates FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON form_templates
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE DELETE ON form_templates FROM screenhire_app;

            ALTER TABLE form_template_versions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE form_template_versions FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_read ON form_template_versions FOR SELECT
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_insert ON form_template_versions FOR INSERT
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_update_drafts ON form_template_versions FOR UPDATE
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND status = 'Draft')
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_delete_drafts ON form_template_versions FOR DELETE
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND status = 'Draft');
            """;

        // Each own template's current versions, oldest first: the newest published one, then the draft. Every follow-up id of
        // a template is one library form, named by its newest name; each different content (name, questions, sections) is
        // one published version of it, in that order, so a draft that changed a form gives it a second version. The
        // follow-up then names the form and the version it holds. Older published versions and jobs keep their copies. A
        // job's follow-up whose id neither current version of its template holds (made in the job's own editor, or on a
        // job without a template) becomes a library form of its own, unless a library version has the same content
        // already; the job keeps its copy as its own. The tables are forced, so with no tenant set the owner reads and
        // writes nothing unless the force is lifted around the copy; it is put back before the migration ends, and the
        // copy aborts it when a current follow-up was left unlinked.
        private const string ConvertFollowUps = """
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE form_templates NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE form_template_versions NO FORCE ROW LEVEL SECURITY;
            CREATE TEMP TABLE follow_up_forms (template_id uuid, follow_up_id text, form_id uuid) ON COMMIT DROP;
            CREATE FUNCTION pg_temp.form_version_of(form uuid, organization uuid, body jsonb, at timestamptz, by_user uuid)
                RETURNS int LANGUAGE plpgsql AS $f$
            DECLARE
                number int;
            BEGIN
                SELECT fv.version INTO number FROM form_template_versions fv WHERE fv.form_template_id = form AND fv.content = body;
                IF number IS NULL THEN
                    SELECT COALESCE(max(fv.version), 0) + 1 INTO number FROM form_template_versions fv WHERE fv.form_template_id = form;
                    INSERT INTO form_template_versions (id, organization_id, form_template_id, version, status, content, created_at, created_by, published_at, published_by)
                    VALUES (gen_random_uuid(), organization, form, number, 'Published', body, at, by_user, at, by_user);
                    UPDATE form_templates
                    SET name = left(body ->> 'name', 80), latest_published_version = number, updated_at = GREATEST(updated_at, at)
                    WHERE id = form;
                END IF;
                RETURN number;
            END
            $f$;
            CREATE FUNCTION pg_temp.form_content(follow_up jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $f$
                SELECT jsonb_strip_nulls(jsonb_build_object(
                    'name', follow_up -> 'name', 'questions', follow_up -> 'questions', 'sections', follow_up -> 'sections'))
            $f$;
            DO $$
            DECLARE
                v record;
                j record;
                follow_ups jsonb;
                follow_up jsonb;
                body jsonb;
                form uuid;
                number int;
                i int;
            BEGIN
                FOR v IN
                    SELECT tv.id, tv.organization_id, tv.template_id, tv.definition, tv.created_by,
                           COALESCE(tv.published_at, tv.created_at) AS at
                    FROM hiring_template_versions tv
                    JOIN hiring_templates t ON t.id = tv.template_id
                    WHERE t.organization_id IS NOT NULL
                      AND (tv.status = 'Draft' OR tv.version = t.latest_published_version)
                      AND jsonb_typeof(tv.definition -> 'workflow' -> 'followUpQuestionnaires') = 'array'
                    ORDER BY tv.template_id, tv.version
                LOOP
                    follow_ups := v.definition -> 'workflow' -> 'followUpQuestionnaires';
                    FOR i IN 0 .. jsonb_array_length(follow_ups) - 1 LOOP
                        follow_up := follow_ups -> i;
                        body := pg_temp.form_content(follow_up);
                        SELECT m.form_id INTO form FROM follow_up_forms m
                        WHERE m.template_id = v.template_id AND m.follow_up_id = follow_up ->> 'id';
                        IF form IS NULL THEN
                            form := gen_random_uuid();
                            INSERT INTO form_templates (id, organization_id, name, kind, created_at, updated_at)
                            VALUES (form, v.organization_id, left(follow_up ->> 'name', 80), 'Standard', v.at, v.at);
                            INSERT INTO follow_up_forms VALUES (v.template_id, follow_up ->> 'id', form);
                        END IF;
                        number := pg_temp.form_version_of(form, v.organization_id, body, v.at, v.created_by);
                        follow_ups := jsonb_set(follow_ups, ARRAY[i::text],
                            follow_up || jsonb_build_object('formTemplateId', form, 'formVersion', number));
                    END LOOP;
                    UPDATE hiring_template_versions
                    SET definition = jsonb_set(definition, '{workflow,followUpQuestionnaires}', follow_ups)
                    WHERE id = v.id;
                END LOOP;

                FOR j IN
                    SELECT jb.organization_id, jb.source_template_id, jb.created_at, f.value AS follow_up
                    FROM jobs jb
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE WHEN jsonb_typeof(jb.workflow -> 'followUpQuestionnaires') = 'array'
                             THEN jb.workflow -> 'followUpQuestionnaires' ELSE '[]'::jsonb END) AS f(value)
                    WHERE NOT EXISTS (
                        SELECT FROM follow_up_forms m
                        WHERE m.template_id = jb.source_template_id AND m.follow_up_id = f.value ->> 'id')
                    ORDER BY jb.organization_id, jb.created_at, jb.id
                LOOP
                    body := pg_temp.form_content(j.follow_up);
                    IF NOT EXISTS (
                        SELECT FROM form_template_versions fv WHERE fv.organization_id = j.organization_id AND fv.content = body) THEN
                        form := gen_random_uuid();
                        INSERT INTO form_templates (id, organization_id, name, kind, created_at, updated_at)
                        VALUES (form, j.organization_id, left(j.follow_up ->> 'name', 80), 'Standard', j.created_at, j.created_at);
                        PERFORM pg_temp.form_version_of(form, j.organization_id, body, j.created_at, NULL);
                    END IF;
                END LOOP;

                IF EXISTS (
                    SELECT FROM hiring_template_versions tv
                    JOIN hiring_templates t ON t.id = tv.template_id
                    CROSS JOIN LATERAL jsonb_array_elements(tv.definition -> 'workflow' -> 'followUpQuestionnaires') AS f(value)
                    WHERE t.organization_id IS NOT NULL
                      AND (tv.status = 'Draft' OR tv.version = t.latest_published_version)
                      AND jsonb_typeof(tv.definition -> 'workflow' -> 'followUpQuestionnaires') = 'array'
                      AND (f.value -> 'formTemplateId' IS NULL OR f.value -> 'formVersion' IS NULL)) THEN
                    RAISE EXCEPTION 'A current hiring template version has a follow-up questionnaire that did not become a library form.';
                END IF;
            END $$;
            ALTER TABLE form_template_versions FORCE ROW LEVEL SECURITY;
            ALTER TABLE form_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "form_template_id",
                table: "questionnaire_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "questionnaire_requests",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.CreateTable(
                name: "form_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    copied_from_form_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latest_published_version = table.Column<int>(type: "integer", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_templates", x => x.id);
                    table.UniqueConstraint("ak_form_templates_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_form_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_template_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_template_versions_form_templates_organization_id_form_",
                        columns: x => new { x.organization_id, x.form_template_id },
                        principalTable: "form_templates",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_form_template_versions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_requests_organization_id_kind_form_template_id",
                table: "questionnaire_requests",
                columns: new[] { "organization_id", "kind", "form_template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_form_template_versions_form_template_id",
                table: "form_template_versions",
                column: "form_template_id",
                unique: true,
                filter: "status = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ix_form_template_versions_form_template_id_version",
                table: "form_template_versions",
                columns: new[] { "form_template_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_template_versions_organization_id",
                table: "form_template_versions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_template_versions_organization_id_form_template_id",
                table: "form_template_versions",
                columns: new[] { "organization_id", "form_template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_form_templates_organization_id_name",
                table: "form_templates",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
            migrationBuilder.Sql(ConvertFollowUps);
        }

        /// <inheritdoc />
        // The links the copy added to the hiring template definitions stay: an older release ignores members it does not know.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_template_versions");

            migrationBuilder.DropTable(
                name: "form_templates");

            migrationBuilder.DropIndex(
                name: "ix_questionnaire_requests_organization_id_kind_form_template_id",
                table: "questionnaire_requests");

            migrationBuilder.DropColumn(
                name: "form_template_id",
                table: "questionnaire_requests");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "questionnaire_requests");
        }
    }
}
