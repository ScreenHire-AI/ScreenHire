using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringModel : Migration
    {
        // Tenant isolation for the new tables (CLAUDE.md non-negotiable 6): the jobs block of AddSignInAndTenantIsolation,
        // copied per table. screenhire_app's grants come from that migration's default privileges.
        //
        // Append-only: job versions and the application timeline are records of what happened, so the application role
        // may insert and read them but never change or delete them. Cascades from a deleted organization still work,
        // because referential actions run as the table owner.
        //
        // Hiring templates: a system template has no organization. Everyone may read system rows; only the owning
        // organization may write its own rows, so no tenant can change a system template. A published version is
        // immutable in the database too: the update and delete policies match drafts only, so changing a published row
        // affects no rows.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE job_versions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE job_versions FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON job_versions
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON job_versions FROM screenhire_app;

            ALTER TABLE candidates ENABLE ROW LEVEL SECURITY;
            ALTER TABLE candidates FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON candidates
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE applications ENABLE ROW LEVEL SECURITY;
            ALTER TABLE applications FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON applications
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE application_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_events
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON application_events FROM screenhire_app;

            ALTER TABLE hiring_templates ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            CREATE POLICY system_read ON hiring_templates FOR SELECT
                USING (organization_id IS NULL);
            CREATE POLICY tenant_isolation ON hiring_templates
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE hiring_template_versions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            CREATE POLICY system_read ON hiring_template_versions FOR SELECT
                USING (organization_id IS NULL);
            CREATE POLICY tenant_read ON hiring_template_versions FOR SELECT
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_insert ON hiring_template_versions FOR INSERT
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_update_drafts ON hiring_template_versions FOR UPDATE
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND status = 'Draft')
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY tenant_delete_drafts ON hiring_template_versions FOR DELETE
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid AND status = 'Draft');
            -- Deleting a template would cascade to its published versions, which are meant to be permanent.
            REVOKE DELETE ON hiring_templates FROM screenhire_app;
            """;

        // Foreign key checks bypass row-level security, and a system template has no organization, so a composite tenant
        // key cannot protect these two links. These triggers do instead. They run as the caller, under row-level
        // security, so a row the caller cannot see is treated as missing: a version must belong to its template's
        // organization (no tenant can add a version to a system template or another tenant's), and a job's template
        // source must be a system version or one of its own organization's.
        private const string EnableTemplateGuards = """
            CREATE FUNCTION hiring_template_version_same_organization() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NOT EXISTS (
                    SELECT FROM hiring_templates t
                    WHERE t.id = NEW.template_id AND t.organization_id IS NOT DISTINCT FROM NEW.organization_id) THEN
                    RAISE EXCEPTION 'A template version must belong to its template''s organization.'
                        USING ERRCODE = 'foreign_key_violation';
                END IF;
                RETURN NEW;
            END
            $$;
            CREATE TRIGGER same_organization BEFORE INSERT OR UPDATE OF template_id, organization_id ON hiring_template_versions
                FOR EACH ROW EXECUTE FUNCTION hiring_template_version_same_organization();

            -- Only a newly set source is checked, so ON DELETE SET NULL cascades never trip it.
            CREATE FUNCTION job_template_source_visible() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.source_template_version_id IS NOT NULL
                    AND (TG_OP = 'INSERT'
                         OR NEW.source_template_version_id IS DISTINCT FROM OLD.source_template_version_id
                         OR NEW.organization_id IS DISTINCT FROM OLD.organization_id)
                    AND NOT EXISTS (
                        SELECT FROM hiring_template_versions v
                        WHERE v.id = NEW.source_template_version_id
                          AND (v.organization_id IS NULL OR v.organization_id = NEW.organization_id)) THEN
                    RAISE EXCEPTION 'A job can only use a system template or one of its own organization''s.'
                        USING ERRCODE = 'foreign_key_violation';
                END IF;
                RETURN NEW;
            END
            $$;
            CREATE TRIGGER template_source_visible BEFORE INSERT OR UPDATE OF source_template_version_id, organization_id ON jobs
                FOR EACH ROW EXECUTE FUNCTION job_template_source_visible();
            """;

        // The version trigger goes with its table; the jobs trigger and both functions need dropping.
        private const string DisableTemplateGuards = """
            DROP TRIGGER IF EXISTS template_source_visible ON jobs;
            DROP FUNCTION IF EXISTS job_template_source_visible();
            DROP TRIGGER IF EXISTS same_organization ON hiring_template_versions;
            DROP FUNCTION IF EXISTS hiring_template_version_same_organization();
            """;

        // The old free-text location becomes the city of one structured location, remote jobs included (their text said
        // where candidates may live; a recruiter turns it into country codes on the next edit). Every existing job gets
        // the default workflow (HiringWorkflow.Default, frozen below).
        //
        // jobs has FORCE ROW LEVEL SECURITY, which binds the owner running this migration too: with no app.tenant_id the
        // UPDATE would match no rows, and the DROP COLUMN after it would lose every location. So FORCE is lifted around
        // the UPDATE (transactional DDL: a failure rolls all of it back), and the migration aborts if any text was not
        // carried over.
        private const string BackfillJobs = """
            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
            UPDATE jobs SET locations = CASE
                WHEN btrim(location) = '' THEN '[]'::jsonb
                ELSE jsonb_build_array(jsonb_build_object('city', btrim(location)))
            END;
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM jobs WHERE btrim(location) <> '' AND locations = '[]'::jsonb) THEN
                    RAISE EXCEPTION 'AddHiringModel: job locations were not carried over; aborting before location is dropped.';
                END IF;
            END
            $$;
            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs ALTER COLUMN locations DROP DEFAULT;
            ALTER TABLE jobs ALTER COLUMN applicant_location_requirements DROP DEFAULT;
            ALTER TABLE jobs ALTER COLUMN workflow DROP DEFAULT;
            ALTER TABLE jobs ALTER COLUMN workflow_version DROP DEFAULT;
            """;

        private const string DefaultWorkflow = """
            {"stages":[{"id":"applied","name":"Applied","kind":"Applied"},{"id":"screening","name":"Screening","kind":"Screening","slaDays":3},{"id":"interview","name":"Interview","kind":"Interview"},{"id":"offer","name":"Offer","kind":"Offer"},{"id":"hired","name":"Hired","kind":"Hired"},{"id":"rejected","name":"Rejected","kind":"Rejected"},{"id":"withdrawn","name":"Withdrawn","kind":"Withdrawn"}],"questionnaire":{"questions":[]},"integrityPolicy":{"aiUse":"AllowedWithDisclosure"},"automations":[]}
            """;

        // The system templates ship as data (SystemTemplates holds their ids). They are inserted before row-level security
        // is forced, because the owner is bound by the policies too, and the policies let nobody write a row without an
        // organization. A later migration that adds a system template version turns FORCE off around its insert.
        private const string InsertSystemTemplates = """
            INSERT INTO hiring_templates (id, organization_id, name, description, created_at, latest_published_version) VALUES
                ('0199e8a0-0000-7000-8000-000000000001', NULL, 'Software engineer',
                 'Screening, a technical exercise and interviews, for engineering roles.', '2026-10-20T00:00:00Z', 1),
                ('0199e8a0-0000-7000-8000-000000000002', NULL, 'Hourly role',
                 'A phone screen and one interview, for shift-based roles.', '2026-10-20T00:00:00Z', 1);

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at) VALUES
                ('0199e8a0-0000-7000-8000-000000000101', NULL, '0199e8a0-0000-7000-8000-000000000001', 1, 'Published',
                 '{"workflow":{"stages":[{"id":"applied","name":"Applied","kind":"Applied"},{"id":"screening","name":"Screening","kind":"Screening","slaDays":3},{"id":"technical","name":"Technical exercise","kind":"Assessment","slaDays":7},{"id":"interview","name":"Interviews","kind":"Interview","slaDays":5},{"id":"offer","name":"Offer","kind":"Offer"},{"id":"hired","name":"Hired","kind":"Hired"},{"id":"rejected","name":"Rejected","kind":"Rejected"},{"id":"withdrawn","name":"Withdrawn","kind":"Withdrawn"}],"questionnaire":{"questions":[{"id":"resume","type":"File","label":"Your CV or resume","required":true,"acceptedFileTypes":[".pdf",".docx"]},{"id":"work_authorization","type":"YesNo","label":"Do you have the right to work in the country this job is in?","required":true,"knockout":{"disqualifyingAnswer":false,"reason":"This role needs the right to work in the country it is based in."}},{"id":"experience","type":"SingleChoice","label":"How many years have you worked as a software engineer?","required":true,"options":[{"id":"less_than_3","label":"Less than 3"},{"id":"three_to_five","label":"3 to 5"},{"id":"six_to_nine","label":"6 to 9"},{"id":"ten_plus","label":"10 or more"}]},{"id":"work_sample","type":"Url","label":"A link to code or a project you are proud of","required":false,"helpText":"A repository, a portfolio or a write-up. Optional."},{"id":"notice_period","type":"ShortText","label":"What is your notice period?","required":false,"maxLength":100}]},"integrityPolicy":{"aiUse":"AllowedWithDisclosure"},"automations":[{"id":"right_to_work_review","name":"Queue for review if no right to work","trigger":{"type":"QuestionnaireCompleted"},"conditions":{"match":"All","rules":[{"fact":"answers.work_authorization","operator":"EqualTo","value":"no","negate":false}]},"actions":[{"type":"QueueForRejectionReview","reason":"Answered no to the right-to-work question."}],"enabled":true,"allowAutoReject":false},{"id":"integrity_review","name":"Ask a reviewer to look at integrity findings","trigger":{"type":"IntegrityAssessed"},"conditions":{"match":"Any","rules":[{"fact":"integrity.authenticity","operator":"EqualTo","value":"ReviewSuggested","negate":false},{"fact":"integrity.aiAssistance","operator":"EqualTo","value":"ReviewSuggested","negate":false}]},"actions":[{"type":"AssignReviewer"},{"type":"AddTag","tag":"integrity-review"}],"enabled":true,"allowAutoReject":false}]},"postingDefaults":{"employmentType":"FullTime","workplaceType":"Remote","department":"Engineering","salaryPeriod":"Year","validForDays":30,"description":"<h2>About the role</h2><p>Describe the team, the problems they work on and what the first months look like.</p><h2>What you will do</h2><ul><li>Design, build and ship features end to end.</li><li>Review code and help the team improve how it works.</li></ul><h2>What we are looking for</h2><ul><li>Experience shipping software in production.</li></ul>"}}',
                 '2026-10-20T00:00:00Z', '2026-10-20T00:00:00Z'),
                ('0199e8a0-0000-7000-8000-000000000201', NULL, '0199e8a0-0000-7000-8000-000000000002', 1, 'Published',
                 '{"workflow":{"stages":[{"id":"applied","name":"Applied","kind":"Applied"},{"id":"screening","name":"Phone screen","kind":"Screening","slaDays":2},{"id":"interview","name":"Interview","kind":"Interview","slaDays":3},{"id":"offer","name":"Offer","kind":"Offer","slaDays":2},{"id":"hired","name":"Hired","kind":"Hired"},{"id":"rejected","name":"Rejected","kind":"Rejected"},{"id":"withdrawn","name":"Withdrawn","kind":"Withdrawn"}],"questionnaire":{"questions":[{"id":"work_authorization","type":"YesNo","label":"Do you have the right to work in the country this job is in?","required":true,"knockout":{"disqualifyingAnswer":false,"reason":"This role needs the right to work in the country it is based in."}},{"id":"shifts","type":"MultiChoice","label":"Which shifts can you work?","required":true,"options":[{"id":"weekday_mornings","label":"Weekday mornings"},{"id":"weekday_evenings","label":"Weekday evenings"},{"id":"weekends","label":"Weekends"},{"id":"nights","label":"Nights"}]},{"id":"start_date","type":"ShortText","label":"When could you start?","required":true,"maxLength":100},{"id":"commute","type":"YesNo","label":"Can you get to the workplace for the start of each shift?","required":true},{"id":"resume","type":"File","label":"Your CV or resume","required":false,"helpText":"Optional.","acceptedFileTypes":[".pdf",".docx",".jpg",".png"]}]},"integrityPolicy":{"aiUse":"Allowed"},"automations":[{"id":"right_to_work_review","name":"Queue for review if no right to work","trigger":{"type":"QuestionnaireCompleted"},"conditions":{"match":"All","rules":[{"fact":"answers.work_authorization","operator":"EqualTo","value":"no","negate":false}]},"actions":[{"type":"QueueForRejectionReview","reason":"Answered no to the right-to-work question."}],"enabled":true,"allowAutoReject":false},{"id":"notify_team","name":"Tell the team about new applications","trigger":{"type":"ApplicationSubmitted"},"actions":[{"type":"NotifyTeam","note":"A new application arrived."}],"enabled":true,"allowAutoReject":false}]},"postingDefaults":{"employmentType":"PartTime","workplaceType":"Onsite","salaryPeriod":"Hour","validForDays":21,"description":"<h2>About the role</h2><p>Describe the work, the place and the team.</p><h2>Shifts and pay</h2><p>Say which shifts are available and how pay works.</p>"}}',
                 '2026-10-20T00:00:00Z', '2026-10-20T00:00:00Z');
            """;

        // Best effort for a rollback: the first location's city, or nothing.
        // FORCE is lifted for the same reason as in BackfillJobs.
        private const string RestoreLocation = """
            ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
            UPDATE jobs SET location = left(COALESCE(locations -> 0 ->> 'city', ''), 200);
            ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
            ALTER TABLE jobs ALTER COLUMN location DROP DEFAULT;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "applicant_location_requirements",
                table: "jobs",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "department",
                table: "jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "jobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "locations",
                table: "jobs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<int>(
                name: "published_version",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "salary_period",
                table: "jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_template_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_template_version",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_template_version_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "valid_through",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workflow",
                table: "jobs",
                type: "jsonb",
                nullable: false,
                defaultValue: DefaultWorkflow.Trim());

            migrationBuilder.AddColumn<int>(
                name: "workflow_version",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // xmin is a system column: Npgsql emits no SQL for it, but the model records it as the row version.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "jobs",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.Sql(BackfillJobs);

            migrationBuilder.DropColumn(
                name: "location",
                table: "jobs");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_jobs_organization_id_id",
                table: "jobs",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateTable(
                name: "candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidates", x => x.id);
                    table.UniqueConstraint("ak_candidates_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_candidates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hiring_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    latest_published_version = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hiring_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_hiring_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_versions_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_stage_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    workflow_version = table.Column<int>(type: "integer", nullable: false),
                    accommodation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    ai_use_declaration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ai_use_details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stage_entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
                    table.UniqueConstraint("ak_applications_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_applications_candidates_organization_id_candidate_id",
                        columns: x => new { x.organization_id, x.candidate_id },
                        principalTable: "candidates",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_applications_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hiring_template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hiring_template_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_hiring_template_versions_hiring_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "hiring_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hiring_template_versions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_events_applications_organization_id_application",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_organization_id_source_template_id",
                table: "jobs",
                columns: new[] { "organization_id", "source_template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_source_template_id",
                table: "jobs",
                column: "source_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_source_template_version_id",
                table: "jobs",
                column: "source_template_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_events_organization_id_application_id_occurred_",
                table: "application_events",
                columns: new[] { "organization_id", "application_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_organization_id_candidate_id",
                table: "applications",
                columns: new[] { "organization_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_organization_id_job_id_candidate_id",
                table: "applications",
                columns: new[] { "organization_id", "job_id", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_organization_id_job_id_current_stage_id",
                table: "applications",
                columns: new[] { "organization_id", "job_id", "current_stage_id" });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_organization_id_normalized_email",
                table: "candidates",
                columns: new[] { "organization_id", "normalized_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hiring_template_versions_organization_id",
                table: "hiring_template_versions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_hiring_template_versions_template_id",
                table: "hiring_template_versions",
                column: "template_id",
                unique: true,
                filter: "status = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "ix_hiring_template_versions_template_id_version",
                table: "hiring_template_versions",
                columns: new[] { "template_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hiring_templates_organization_id_name",
                table: "hiring_templates",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_job_versions_organization_id_job_id_version",
                table: "job_versions",
                columns: new[] { "organization_id", "job_id", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_jobs_hiring_template_versions_source_template_version_id",
                table: "jobs",
                column: "source_template_version_id",
                principalTable: "hiring_template_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_jobs_hiring_templates_source_template_id",
                table: "jobs",
                column: "source_template_id",
                principalTable: "hiring_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(InsertSystemTemplates);
            migrationBuilder.Sql(EnableRowLevelSecurity);
            migrationBuilder.Sql(EnableTemplateGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants with them.
            migrationBuilder.Sql(DisableTemplateGuards);

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(RestoreLocation);

            migrationBuilder.DropForeignKey(
                name: "fk_jobs_hiring_template_versions_source_template_version_id",
                table: "jobs");

            migrationBuilder.DropForeignKey(
                name: "fk_jobs_hiring_templates_source_template_id",
                table: "jobs");

            migrationBuilder.DropTable(
                name: "application_events");

            migrationBuilder.DropTable(
                name: "hiring_template_versions");

            migrationBuilder.DropTable(
                name: "job_versions");

            migrationBuilder.DropTable(
                name: "applications");

            migrationBuilder.DropTable(
                name: "hiring_templates");

            migrationBuilder.DropTable(
                name: "candidates");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_jobs_organization_id_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_organization_id_source_template_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_source_template_id",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_source_template_version_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "applicant_location_requirements",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "department",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "description",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "locations",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "published_version",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "salary_period",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "source_template_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "source_template_version",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "source_template_version_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "valid_through",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "workflow",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "workflow_version",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "jobs");
        }
    }
}
