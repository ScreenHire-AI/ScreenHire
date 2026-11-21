using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Named teams (docs/architecture/sign-in-and-tenancy.md, "Job access"): <c>named_teams</c> holds the organization's teams,
    /// whose people a new job or a job's Hiring team copies, and <c>jobs.started_with_team_id</c> the team a job started with,
    /// for the next job's suggestion. Hiring templates lose their default hiring team: each own template's non-empty list of
    /// current members becomes a team named after it, so nobody's setup is lost, before the column goes.
    /// </summary>
    public partial class AddNamedTeams : Migration
    {
        // Tenant row-level security like every tenant table (AddSignInAndTenantIsolation). The table has no job_id, so it needs no
        // job_scope policy: a team gives nobody access to a job until its people are copied onto one.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE named_teams ENABLE ROW LEVEL SECURITY;
            ALTER TABLE named_teams FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON named_teams
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // Each own template, archived ones included, whose default team still names a current member of its organization becomes
        // "<template name> team" with those members. Template names are not unique, so a second template of the same name, in any
        // case, gets " 2", the next " 3", oldest first: team names are unique ignoring case, as the API checks them. Template
        // names are at most 120 characters, so every name fits the 140 of the column. upper() is the database's, where the API
        // uses ToUpperInvariant: they agree on every name but rare non-ASCII ones, where a later rename may meet a near-twin
        // the index lets through, which harms nothing. People who left are dropped, as a job's
        // team drops them. The tables are forced, so with no tenant set the owner reads and writes nothing unless the force is
        // lifted around the copy. It is put back before the migration ends, and the copy aborts it when a team was missed, before
        // the column is dropped.
        private const string ConvertDefaultTeams = """
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE organization_members NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE named_teams NO FORCE ROW LEVEL SECURITY;
            WITH kept AS (
                SELECT t.id, t.organization_id, t.created_at, btrim(t.name) || ' team' AS base,
                       ARRAY(SELECT DISTINCT m.id FROM organization_members m
                             WHERE m.organization_id = t.organization_id AND m.id = ANY (t.default_hiring_team)) AS people
                FROM hiring_templates t
                WHERE t.organization_id IS NOT NULL
            ), numbered AS (
                SELECT k.*, row_number() OVER (PARTITION BY k.organization_id, upper(k.base) ORDER BY k.created_at, k.id) AS n
                FROM kept k
                WHERE cardinality(k.people) > 0
            ), named AS (
                SELECT organization_id, people, CASE WHEN n = 1 THEN base ELSE base || ' ' || n END AS name
                FROM numbered
            )
            INSERT INTO named_teams (id, organization_id, name, normalized_name, member_ids, created_at)
            SELECT gen_random_uuid(), organization_id, name, upper(name), people, now()
            FROM named;
            DO $$
            BEGIN
                IF (SELECT count(*) FROM named_teams) <> (
                    SELECT count(*) FROM hiring_templates t
                    WHERE t.organization_id IS NOT NULL
                      AND EXISTS (SELECT 1 FROM organization_members m
                                  WHERE m.organization_id = t.organization_id AND m.id = ANY (t.default_hiring_team))) THEN
                    RAISE EXCEPTION 'The hiring templates'' default teams were not all kept as named teams.';
                END IF;
            END $$;
            ALTER TABLE named_teams FORCE ROW LEVEL SECURITY;
            ALTER TABLE organization_members FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "named_teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    member_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_named_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_named_teams_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_named_teams_organization_id_normalized_name",
                table: "named_teams",
                columns: new[] { "organization_id", "normalized_name" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
            migrationBuilder.Sql(ConvertDefaultTeams);

            migrationBuilder.DropColumn(
                name: "default_hiring_team",
                table: "hiring_templates");

            migrationBuilder.AddColumn<Guid>(
                name: "started_with_team_id",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_started_with_team_id",
                table: "jobs",
                column: "started_with_team_id");

            migrationBuilder.AddForeignKey(
                name: "fk_jobs_named_teams_started_with_team_id",
                table: "jobs",
                column: "started_with_team_id",
                principalTable: "named_teams",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <summary>
        /// Drops the teams and gives templates back an empty default team: a team's people are not written back to a template,
        /// because a team made or renamed since names no template.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_jobs_named_teams_started_with_team_id",
                table: "jobs");

            migrationBuilder.DropTable(
                name: "named_teams");

            migrationBuilder.DropIndex(
                name: "ix_jobs_started_with_team_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "started_with_team_id",
                table: "jobs");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "default_hiring_team",
                table: "hiring_templates",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");
        }
    }
}
