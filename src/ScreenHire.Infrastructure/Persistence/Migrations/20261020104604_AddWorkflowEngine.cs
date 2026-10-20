using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEngine : Migration
    {
        // Tenant isolation for the workflow engine's tables (CLAUDE.md non-negotiable 6): the block from AddHiringModel,
        // per table. screenhire_app's grants come from AddSignInAndTenantIsolation's default privileges. The worker
        // updates run and action statuses, so these tables are not append-only; the application timeline stays so.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE automation_runs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE automation_runs FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON automation_runs
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            -- The audit record of what an automation did is kept: the application role may not delete it.
            REVOKE DELETE ON automation_runs FROM screenhire_app;

            ALTER TABLE automation_action_runs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE automation_action_runs FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON automation_action_runs
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE DELETE ON automation_action_runs FROM screenhire_app;

            ALTER TABLE application_tasks ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_tasks FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_tasks
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "tags",
                table: "applications",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "application_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    automation_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_tasks_applications_organization_id_application_",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    automation_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    automation_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    workflow_version = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    trigger_stage_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    caused_by_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chain = table.Column<List<string>>(type: "text[]", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facts = table.Column<string>(type: "jsonb", nullable: false),
                    rule_results = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_runs", x => x.id);
                    table.UniqueConstraint("ak_automation_runs_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_automation_runs_applications_organization_id_application_id",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automation_action_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_action_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_automation_action_runs_automation_runs_organization_id_run_",
                        columns: x => new { x.organization_id, x.run_id },
                        principalTable: "automation_runs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_tasks_organization_id_application_id",
                table: "application_tasks",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_automation_action_runs_organization_id_run_id_index",
                table: "automation_action_runs",
                columns: new[] { "organization_id", "run_id", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automation_runs_organization_id_application_id_automation_i",
                table: "automation_runs",
                columns: new[] { "organization_id", "application_id", "automation_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automation_runs_organization_id_status_created_at",
                table: "automation_runs",
                columns: new[] { "organization_id", "status", "created_at" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants with them.
            migrationBuilder.DropTable(
                name: "application_tasks");

            migrationBuilder.DropTable(
                name: "automation_action_runs");

            migrationBuilder.DropTable(
                name: "automation_runs");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "applications");
        }
    }
}
