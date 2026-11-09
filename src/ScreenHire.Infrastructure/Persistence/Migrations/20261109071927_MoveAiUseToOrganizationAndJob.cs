using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The AI-use policy is the organization's, and a job may override it, instead of every job and template carrying a
    /// copy. Each existing job's own copy (<c>workflow.verificationPolicy.aiUse</c>) becomes its override where it differs
    /// from what the organization's policy is now, so no job changes what its apply form asks. Published template versions
    /// are immutable and keep the old key, which reading ignores. Down drops the column and the overrides with it.
    /// </summary>
    public partial class MoveAiUseToOrganizationAndJob : Migration
    {
    /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_use_override",
                table: "jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // The migration role must read every organization's settings and write every job, so row-level security is
            // lifted for the two tables and put back at once, as RenameIntegrityToVerification does.
            migrationBuilder.Sql("""
                ALTER TABLE jobs NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE verification_settings NO FORCE ROW LEVEL SECURITY;

                UPDATE jobs j
                SET ai_use_override = j.workflow #>> '{verificationPolicy,aiUse}'
                WHERE j.workflow #>> '{verificationPolicy,aiUse}' IN ('Allowed', 'AllowedWithDisclosure', 'NotAllowed')
                  AND j.workflow #>> '{verificationPolicy,aiUse}' <>
                      COALESCE((SELECT s.default_ai_use FROM verification_settings s WHERE s.organization_id = j.organization_id), 'AllowedWithDisclosure');

                ALTER TABLE verification_settings FORCE ROW LEVEL SECURITY;
                ALTER TABLE jobs FORCE ROW LEVEL SECURITY;
                """);
        }

    /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_use_override",
                table: "jobs");
        }
    }
}
