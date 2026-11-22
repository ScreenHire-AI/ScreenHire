using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefaultFourQuestionsPerPage : Migration
    {
        // A few related questions at a time (apply-flow.md section 7): the default page size goes from 8 to 4. Every
        // organization holds a number, and 8 was the default, so an organization on 8 moves to 4 unless its change history
        // shows someone set questions per page (SettingsChanges writes "changed questions per page from"). Organizations on
        // any other number keep it. Both tables are forced, so with no tenant set the owner sees no rows unless the force is
        // lifted around the update; it is put back before the migration ends.
        private const string MoveDefaultToFour = """
            ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE change_records NO FORCE ROW LEVEL SECURITY;
            UPDATE organizations o SET questions_per_page = 4
                WHERE o.questions_per_page = 8
                  AND NOT EXISTS (
                      SELECT 1 FROM change_records r
                      WHERE r.organization_id = o.id
                        AND r.subject_kind = 'Organization'
                        AND r.lines::text LIKE '%changed questions per page from%');
            ALTER TABLE change_records FORCE ROW LEVEL SECURITY;
            ALTER TABLE organizations FORCE ROW LEVEL SECURITY;
            """;

        // Back to the old default for the organizations the update could have moved; an organization that chose 4 itself
        // keeps it only if its history says so.
        private const string MoveDefaultBackToEight = """
            ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE change_records NO FORCE ROW LEVEL SECURITY;
            UPDATE organizations o SET questions_per_page = 8
                WHERE o.questions_per_page = 4
                  AND NOT EXISTS (
                      SELECT 1 FROM change_records r
                      WHERE r.organization_id = o.id
                        AND r.subject_kind = 'Organization'
                        AND r.lines::text LIKE '%changed questions per page from%');
            ALTER TABLE change_records FORCE ROW LEVEL SECURITY;
            ALTER TABLE organizations FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MoveDefaultToFour);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MoveDefaultBackToEight);
        }
    }
}
