using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantCreateTasksToCustomRoles : Migration
    {
        // Create tasks joined the hiring writes, and a Write key needs every hiring write in its creator's role (ADR-0014).
        // A custom role that held all the others keeps its members' right to create Write keys, and keeps the Write keys
        // they already made within their role, by gaining it. Other custom roles get it only when an Owner adds it. The
        // names are written out because a migration is frozen: RolePermissions.HiringWrites may grow again. FORCE binds
        // the owner running this too, so it is lifted around the update, as in AddCustomRoles.
        private const string Grant = """
            ALTER TABLE custom_roles NO FORCE ROW LEVEL SECURITY;
            UPDATE custom_roles SET permissions = array_append(permissions, 'CreateTasks')
            WHERE permissions @> ARRAY['EditJobs', 'AddCandidates', 'MoveCandidates', 'AddNotesAndTags', 'CompleteTasks',
                    'EmailCandidates', 'FillInScorecards', 'ReviewIntegrity', 'RunAutomations', 'EditHiringTemplates']::text[]
                AND NOT ('CreateTasks' = ANY (permissions));
            ALTER TABLE custom_roles FORCE ROW LEVEL SECURITY;
            """;

        // The code before this migration cannot read a role that holds CreateTasks, so going back takes it from every
        // custom role, including one an Owner gave it since.
        private const string Revoke = """
            ALTER TABLE custom_roles NO FORCE ROW LEVEL SECURITY;
            UPDATE custom_roles SET permissions = array_remove(permissions, 'CreateTasks')
            WHERE 'CreateTasks' = ANY (permissions);
            ALTER TABLE custom_roles FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Grant);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Revoke);
    }
}
