using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationRunRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_verification_review_actions_one_actor",
                table: "verification_review_actions");

            migrationBuilder.DropIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs");

            migrationBuilder.AddColumn<int>(
                name: "source_revision",
                table: "verification_signals",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                table: "verification_analyzer_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_verification_review_actions_one_actor",
                table: "verification_review_actions",
                sql: "((actor_user_id IS NOT NULL) <> (api_key_id IS NOT NULL)) OR (type = 'EvidenceChanged' AND actor_user_id IS NULL AND api_key_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs",
                columns: new[] { "organization_id", "application_id", "analyzer", "analyzer_version", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Once re-checks have run this fails on the rows they wrote: EvidenceChanged review entries break the stricter
            // check, and runs past revision 1 break the old unique index. A rollback after that needs those rows gone first.
            migrationBuilder.DropCheckConstraint(
                name: "ck_verification_review_actions_one_actor",
                table: "verification_review_actions");

            migrationBuilder.DropIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs");

            migrationBuilder.DropColumn(
                name: "source_revision",
                table: "verification_signals");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "verification_analyzer_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_verification_review_actions_one_actor",
                table: "verification_review_actions",
                sql: "(actor_user_id IS NOT NULL) <> (api_key_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_verification_analyzer_runs_organization_id_application_id_a",
                table: "verification_analyzer_runs",
                columns: new[] { "organization_id", "application_id", "analyzer", "analyzer_version" },
                unique: true);
        }
    }
}
