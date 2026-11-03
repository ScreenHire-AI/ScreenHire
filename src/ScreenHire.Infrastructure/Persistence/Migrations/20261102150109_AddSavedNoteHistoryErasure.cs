using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Lets erasing a candidate remove the history of their saved notes. <c>change_records</c> stays append-only for
    /// <c>screenhire_app</c>, which holds no DELETE on it, so the one delete an erasure needs is this function, run with the
    /// owner's rights. A record of a saved note holds the note's free text and is keyed by the entry alone, and an entry that
    /// left the list leaves its records with no link to the candidate. So the function keeps the records of every other
    /// candidate's entry and deletes the rest: the erased person's, and the orphans, which nobody can read.
    /// </summary>
    /// <remarks>
    /// The organization comes from the transaction's <c>app.tenant_id</c>, as in the row-level security policies, never from
    /// an argument, so a caller reaches only its own tenant's rows. The check is written out because a migrating role that is
    /// a superuser passes row-level security. <c>job_scope</c> does not apply to the owner, so every entry of the organization
    /// counts, whatever jobs the caller sees. STRICT because a null argument would keep nothing, not everything:
    /// <c>e.candidate_id &lt;&gt; NULL</c> is never true. <c>pg_temp</c> comes last so a session's temporary table cannot
    /// stand in for <c>talent_pool_entries</c>.
    /// </remarks>
    public partial class AddSavedNoteHistoryErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION erase_saved_note_history(erased_candidate uuid) RETURNS void
                LANGUAGE sql STRICT SECURITY DEFINER SET search_path = pg_catalog, public, pg_temp AS $$
                    DELETE FROM change_records r
                    WHERE r.organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                      AND r.subject_kind = 'TalentPoolEntry'
                      AND NOT EXISTS (
                          SELECT 1 FROM talent_pool_entries e
                          WHERE e.organization_id = r.organization_id
                            AND e.id::text = r.subject_id
                            AND e.candidate_id <> erased_candidate);
                $$;
                REVOKE ALL ON FUNCTION erase_saved_note_history(uuid) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION erase_saved_note_history(uuid) TO screenhire_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS erase_saved_note_history(uuid);");
        }
    }
}
