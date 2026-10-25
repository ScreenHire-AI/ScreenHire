using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAccess : Migration
    {
        // Job access (ADR-0015). Every policy here is RESTRICTIVE, so it is ANDed with tenant_isolation and can only
        // narrow it; a permissive policy would be ORed and widen it. TO screenhire_app: job access is about the member a
        // request acts for, which only the API and the worker (both screenhire_app) know; the owner, who runs migrations,
        // stays bound by tenant_isolation alone. app.job_scope is 'all' for everyone who sees every job and for work with
        // no user (TenantSessionInterceptor); anything else shows a job only to the members on its hiring team. The
        // hiring team is a column of the job's own row, so no policy here reads a table whose policy reads it back.
        // Candidates keep tenant-only row-level security: one person who applied to two jobs is one row, found by email
        // when they apply again, so the API filters them by job instead (ScreenHireDbContext.JobScopeFilter).
        private const string SeesFunctions = """
            CREATE FUNCTION app_sees_all_jobs() RETURNS boolean LANGUAGE sql STABLE AS $$
                SELECT COALESCE(current_setting('app.job_scope', true) = 'all', false)
            $$;
            CREATE FUNCTION app_sees_job(organization uuid, job uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
                SELECT app_sees_all_jobs() OR EXISTS (
                    SELECT 1 FROM jobs j
                    WHERE j.organization_id = organization AND j.id = job
                      AND NULLIF(current_setting('app.member_id', true), '')::uuid = ANY (j.hiring_team))
            $$;
            CREATE FUNCTION app_sees_application(organization uuid, application uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
                SELECT app_sees_all_jobs() OR EXISTS (
                    SELECT 1 FROM applications a
                    JOIN jobs j ON j.organization_id = a.organization_id AND j.id = a.job_id
                    WHERE a.organization_id = organization AND a.id = application
                      AND NULLIF(current_setting('app.member_id', true), '')::uuid = ANY (j.hiring_team))
            $$;
            """;

        private const string EnableJobScope = """
            CREATE POLICY job_scope ON jobs AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_all_jobs() OR NULLIF(current_setting('app.member_id', true), '')::uuid = ANY (hiring_team))
                WITH CHECK (app_sees_all_jobs() OR NULLIF(current_setting('app.member_id', true), '')::uuid = ANY (hiring_team));
            CREATE POLICY job_scope ON job_versions AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON applications AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON application_scorecards AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON automation_runs AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON questionnaire_requests AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON job_postings AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON telemetry_snapshots AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON sandbox_listings AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            CREATE POLICY job_scope ON application_events AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON application_notes AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON application_emails AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON application_files AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON application_tasks AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON integrity_analyzer_runs AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON integrity_signals AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON integrity_assessments AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON integrity_review_actions AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON consistency_checks AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON submission_observations AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON application_documents AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON answer_fingerprints AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            CREATE POLICY job_scope ON application_note_mentions AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_all_jobs() OR EXISTS (SELECT 1 FROM application_notes n
                    WHERE n.organization_id = application_note_mentions.organization_id AND n.id = application_note_mentions.note_id))
                WITH CHECK (app_sees_all_jobs() OR EXISTS (SELECT 1 FROM application_notes n
                    WHERE n.organization_id = application_note_mentions.organization_id AND n.id = application_note_mentions.note_id));
            CREATE POLICY job_scope ON automation_action_runs AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_all_jobs() OR EXISTS (SELECT 1 FROM automation_runs r
                    WHERE r.organization_id = automation_action_runs.organization_id AND r.id = automation_action_runs.run_id))
                WITH CHECK (app_sees_all_jobs() OR EXISTS (SELECT 1 FROM automation_runs r
                    WHERE r.organization_id = automation_action_runs.organization_id AND r.id = automation_action_runs.run_id));
            CREATE POLICY job_scope ON indexing_notifications AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_all_jobs() OR EXISTS (SELECT 1 FROM job_postings p
                    WHERE p.organization_id = indexing_notifications.organization_id AND p.id = indexing_notifications.posting_id))
                WITH CHECK (app_sees_all_jobs() OR EXISTS (SELECT 1 FROM job_postings p
                    WHERE p.organization_id = indexing_notifications.organization_id AND p.id = indexing_notifications.posting_id));
            CREATE POLICY job_scope ON change_records AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (CASE WHEN subject_kind = 'Job' THEN app_sees_job(organization_id, subject_id::uuid) ELSE true END)
                WITH CHECK (CASE WHEN subject_kind = 'Job' THEN app_sees_job(organization_id, subject_id::uuid) ELSE true END);
            """;

        private const string DisableJobScope = """
            DROP POLICY IF EXISTS job_scope ON jobs;
            DROP POLICY IF EXISTS job_scope ON job_versions;
            DROP POLICY IF EXISTS job_scope ON applications;
            DROP POLICY IF EXISTS job_scope ON application_scorecards;
            DROP POLICY IF EXISTS job_scope ON automation_runs;
            DROP POLICY IF EXISTS job_scope ON questionnaire_requests;
            DROP POLICY IF EXISTS job_scope ON job_postings;
            DROP POLICY IF EXISTS job_scope ON telemetry_snapshots;
            DROP POLICY IF EXISTS job_scope ON sandbox_listings;
            DROP POLICY IF EXISTS job_scope ON application_events;
            DROP POLICY IF EXISTS job_scope ON application_notes;
            DROP POLICY IF EXISTS job_scope ON application_emails;
            DROP POLICY IF EXISTS job_scope ON application_files;
            DROP POLICY IF EXISTS job_scope ON application_tasks;
            DROP POLICY IF EXISTS job_scope ON integrity_analyzer_runs;
            DROP POLICY IF EXISTS job_scope ON integrity_signals;
            DROP POLICY IF EXISTS job_scope ON integrity_assessments;
            DROP POLICY IF EXISTS job_scope ON integrity_review_actions;
            DROP POLICY IF EXISTS job_scope ON consistency_checks;
            DROP POLICY IF EXISTS job_scope ON submission_observations;
            DROP POLICY IF EXISTS job_scope ON application_documents;
            DROP POLICY IF EXISTS job_scope ON answer_fingerprints;
            DROP POLICY IF EXISTS job_scope ON application_note_mentions;
            DROP POLICY IF EXISTS job_scope ON automation_action_runs;
            DROP POLICY IF EXISTS job_scope ON indexing_notifications;
            DROP POLICY IF EXISTS job_scope ON change_records;
            DROP FUNCTION IF EXISTS app_sees_application(uuid, uuid);
            DROP FUNCTION IF EXISTS app_sees_job(uuid, uuid);
            DROP FUNCTION IF EXISTS app_sees_all_jobs();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "job_access",
                table: "organization_members",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "All");

            migrationBuilder.AddColumn<string>(
                name: "job_access",
                table: "organization_invitations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "All");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "membership_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_api_key_id",
                table: "membership_events",
                type: "uuid",
                nullable: true);

            // The log keeps ids only, like the member and actor ids beside them; titles are read through the job.
            migrationBuilder.AddColumn<Guid>(
                name: "hiring_template_id",
                table: "membership_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_access",
                table: "membership_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "job_id",
                table: "membership_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_job_access",
                table: "membership_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Existing jobs start with an empty hiring team and existing members with All jobs, so nobody's access changes.
            migrationBuilder.AddColumn<List<Guid>>(
                name: "hiring_team",
                table: "jobs",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "default_hiring_team",
                table: "hiring_templates",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_members_job_access",
                table: "organization_members",
                sql: "job_access IN ('All', 'HiringTeamOnly')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_invitations_job_access",
                table: "organization_invitations",
                sql: "job_access IN ('All', 'HiringTeamOnly')");

            migrationBuilder.Sql(SeesFunctions);
            migrationBuilder.Sql(EnableJobScope);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableJobScope);

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_members_job_access",
                table: "organization_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_invitations_job_access",
                table: "organization_invitations");

            migrationBuilder.DropColumn(
                name: "job_access",
                table: "organization_members");

            migrationBuilder.DropColumn(
                name: "job_access",
                table: "organization_invitations");

            migrationBuilder.DropColumn(
                name: "actor_api_key_id",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "hiring_template_id",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "job_access",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "previous_job_access",
                table: "membership_events");

            migrationBuilder.DropColumn(
                name: "hiring_team",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "default_hiring_team",
                table: "hiring_templates");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "membership_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
