using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Four follow-ups from the last workflow: trigram indexes for the search box, a foreign key from a review action
    /// to its signal, <c>change_records.created</c> for records written before that column, and <c>brand_images.saved_at</c>.
    /// The owner runs this. Row-level security is FORCEd on the tables below, so a backfill or a check that reads them
    /// as a role that does not bypass it sees no rows with no tenant set: each is wrapped in NO FORCE / FORCE and ends
    /// by raising if a row was missed (the pattern of <c>AddHiringModel</c>'s backfill).
    /// </summary>
    public partial class HardenFromWorkflowFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "saved_at",
                table: "brand_images",
                type: "timestamp with time zone",
                nullable: true);

            // Every image uploaded so far counts as saved: a replaced logo is still linked from emails already sent,
            // and nothing records which old uploads were once in use, so none of them may be cleaned up. Only uploads
            // made from now on can be abandoned.
            migrationBuilder.Sql("""
                ALTER TABLE brand_images NO FORCE ROW LEVEL SECURITY;
                UPDATE brand_images SET saved_at = uploaded_at WHERE saved_at IS NULL;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM brand_images WHERE saved_at IS NULL) THEN
                        RAISE EXCEPTION 'brand_images.saved_at was not backfilled for every row';
                    END IF;
                END $$;
                ALTER TABLE brand_images FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_verification_signals_organization_id_id",
                table: "verification_signals",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_review_actions_organization_id_signal_id",
                table: "verification_review_actions",
                columns: new[] { "organization_id", "signal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_title",
                table: "jobs",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_email",
                table: "candidates",
                column: "email")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_full_name",
                table: "candidates",
                column: "full_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_brand_images_organization_id_uploaded_at",
                table: "brand_images",
                columns: new[] { "organization_id", "uploaded_at" },
                filter: "saved_at is null");

            // A signal id that points at no signal of the same organization would fail the key below with a bare
            // constraint error. Say which rows instead, and stop: the audit trail is not ours to rewrite.
            migrationBuilder.Sql("""
                ALTER TABLE verification_review_actions NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE verification_signals NO FORCE ROW LEVEL SECURITY;
                DO $$
                DECLARE orphans integer;
                BEGIN
                    SELECT count(*) INTO orphans
                    FROM verification_review_actions a
                    WHERE a.signal_id IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM verification_signals s
                          WHERE s.organization_id = a.organization_id AND s.id = a.signal_id);
                    IF orphans > 0 THEN
                        RAISE EXCEPTION '% verification review actions name a signal that does not exist', orphans;
                    END IF;
                END $$;
                ALTER TABLE verification_signals FORCE ROW LEVEL SECURITY;
                ALTER TABLE verification_review_actions FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_review_actions_verification_signals_organizati",
                table: "verification_review_actions",
                columns: new[] { "organization_id", "signal_id" },
                principalTable: "verification_signals",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Cascade);

            // Records written before the created column (AddChangeRecordCreated) all read created = false, including the
            // saves that made the object. Those saves are recognisable by their first line, which the describers in
            // ScreenHire.Domain.History write only for a creation, in the kinds whose interceptor rule sets Created
            // (TrackedKinds). An API key's "created the key" is left out: keys never set Created. The text is the
            // describers' own, so a record that merely mentions the phrase later in its lines does not match.
            migrationBuilder.Sql("""
                ALTER TABLE change_records NO FORCE ROW LEVEL SECURITY;
                UPDATE change_records
                SET created = true
                WHERE created = false AND ({0});
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM change_records WHERE created = false AND ({0})) THEN
                        RAISE EXCEPTION 'change_records.created was not backfilled for every creation record';
                    END IF;
                END $$;
                ALTER TABLE change_records FORCE ROW LEVEL SECURITY;
                """.Replace("{0}", CreationRecords));
        }

        // The records a creation wrote, by subject kind and the first line's first run.
        private const string CreationRecords = """
            (subject_kind = 'HiringTemplate' AND lines->0->'parts'->0->>'text' LIKE 'created the template%')
            OR (subject_kind = 'EmailTemplate' AND lines->0->'parts'->0->>'text' LIKE 'created the template%')
            OR (subject_kind = 'Job' AND lines->0->'parts'->0->>'text' LIKE 'created the job%')
            OR (subject_kind = 'ScorecardTemplate' AND lines->0->'parts'->0->>'text' LIKE 'created the scorecard%')
            OR (subject_kind = 'Role' AND lines->0->'parts'->0->>'text' LIKE 'created the role from %')
            OR (subject_kind = 'Team' AND lines->0->'parts'->0->>'text' LIKE 'created the team%')
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The two backfills are not undone: a created flag that is right stays right.
            migrationBuilder.DropForeignKey(
                name: "fk_verification_review_actions_verification_signals_organizati",
                table: "verification_review_actions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_verification_signals_organization_id_id",
                table: "verification_signals");

            migrationBuilder.DropIndex(
                name: "ix_verification_review_actions_organization_id_signal_id",
                table: "verification_review_actions");

            migrationBuilder.DropIndex(
                name: "ix_jobs_title",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_candidates_email",
                table: "candidates");

            migrationBuilder.DropIndex(
                name: "ix_candidates_full_name",
                table: "candidates");

            migrationBuilder.DropIndex(
                name: "ix_brand_images_organization_id_uploaded_at",
                table: "brand_images");

            migrationBuilder.DropColumn(
                name: "saved_at",
                table: "brand_images");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
