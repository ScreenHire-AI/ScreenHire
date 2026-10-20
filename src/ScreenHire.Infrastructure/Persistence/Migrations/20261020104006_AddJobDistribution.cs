using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobDistribution : Migration
    {
        // Tenant isolation for the new tables (CLAUDE.md non-negotiable 6): the jobs block of AddSignInAndTenantIsolation,
        // copied per table. screenhire_app's grants come from that migration's default privileges.
        //
        // indexing_quota_days has no organization and no policy: one row per UTC day with the deployment-wide count of
        // Google Indexing API calls, which the dispatcher must read and update across tenants.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE job_postings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE job_postings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON job_postings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE indexing_notifications ENABLE ROW LEVEL SECURITY;
            ALTER TABLE indexing_notifications FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON indexing_notifications
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE sandbox_listings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sandbox_listings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON sandbox_listings
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "indeed_eligible",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_trial",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "indexing_quota_days",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    used = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexing_quota_days", x => x.day);
                });

            migrationBuilder.CreateTable(
                name: "job_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    job_version = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issues = table.Column<List<string>>(type: "text[]", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    expected_by = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    live_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_postings", x => x.id);
                    table.UniqueConstraint("ak_job_postings_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_job_postings_jobs_organization_id_job_id",
                        columns: x => new { x.organization_id, x.job_id },
                        principalTable: "jobs",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_version = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removal_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sandbox_listings", x => x.id);
                    table.ForeignKey(
                        name: "fk_sandbox_listings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexing_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexing_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_indexing_notifications_job_postings_organization_id_posting",
                        columns: x => new { x.organization_id, x.posting_id },
                        principalTable: "job_postings",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_indexing_notifications_organization_id_posting_id",
                table: "indexing_notifications",
                columns: new[] { "organization_id", "posting_id" });

            migrationBuilder.CreateIndex(
                name: "ix_indexing_notifications_organization_id_sent_at_superseded_at",
                table: "indexing_notifications",
                columns: new[] { "organization_id", "sent_at", "superseded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_organization_id_board_id_state",
                table: "job_postings",
                columns: new[] { "organization_id", "board_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_organization_id_job_id_board_id",
                table: "job_postings",
                columns: new[] { "organization_id", "job_id", "board_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_organization_id_next_check_at",
                table: "job_postings",
                columns: new[] { "organization_id", "next_check_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_listings_organization_id_posting_id",
                table: "sandbox_listings",
                columns: new[] { "organization_id", "posting_id" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indexing_notifications");

            migrationBuilder.DropTable(
                name: "indexing_quota_days");

            migrationBuilder.DropTable(
                name: "sandbox_listings");

            migrationBuilder.DropTable(
                name: "job_postings");

            migrationBuilder.DropColumn(
                name: "indeed_eligible",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "is_trial",
                table: "organizations");
        }
    }
}
