using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Postings the team made themselves on boards we do not integrate with (docs/architecture/job-distribution.md, "Posted by
    /// you"). A tenant table under one job: tenant row-level security like every tenant table (AddSignInAndTenantIsolation)
    /// and the restrictive job_scope policy of the rows under a job (AddJobAccess).
    /// </summary>
    public partial class AddManualPostings : Migration
    {
        private const string EnableRowLevelSecurity = """
            ALTER TABLE manual_postings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE manual_postings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON manual_postings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON manual_postings AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_job(organization_id, job_id)) WITH CHECK (app_sees_job(organization_id, job_id));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    listing_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    posted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    posted_content = table.Column<string>(type: "jsonb", nullable: false),
                    content_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    taken_down_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_postings", x => x.id);
                    table.ForeignKey(
                        name: "fk_manual_postings_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manual_postings_organization_id_job_id_source_key",
                table: "manual_postings",
                columns: new[] { "organization_id", "job_id", "source_key" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_postings");
        }
    }
}
