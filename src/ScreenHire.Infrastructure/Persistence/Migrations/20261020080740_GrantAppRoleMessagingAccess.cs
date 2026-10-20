using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantAppRoleMessagingAccess : Migration
    {
        private const string Schema = DatabaseRegistration.MessagingSchema;

        // Wolverine's envelope and queue tables live in their own schema, outside the EF model. The owner creates them
        // (ScreenHireMessaging.ProvisionStorageAsync, run after this migration); screenhire_app, which the API and worker
        // connect as, only reads and writes rows. The tables have no row-level security: they hold message envelopes, not
        // tenant rows, and messages carry ids rather than tenant data (docs/architecture/messaging.md).
        // Default privileges cover the tables the owner creates later, including after a Wolverine upgrade; the plain
        // grants cover tables an earlier version created before this migration existed.
        private const string GrantMessagingAccess = $"""
            CREATE SCHEMA IF NOT EXISTS {Schema};
            GRANT USAGE ON SCHEMA {Schema} TO screenhire_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {Schema} TO screenhire_app;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {Schema} TO screenhire_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA {Schema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO screenhire_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA {Schema} GRANT USAGE, SELECT ON SEQUENCES TO screenhire_app;
            """;

        // The schema stays: it holds Wolverine's messages, which a rollback of this grant must not delete.
        private const string RevokeMessagingAccess = $"""
            ALTER DEFAULT PRIVILEGES IN SCHEMA {Schema} REVOKE ALL ON TABLES FROM screenhire_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA {Schema} REVOKE ALL ON SEQUENCES FROM screenhire_app;
            REVOKE ALL ON ALL TABLES IN SCHEMA {Schema} FROM screenhire_app;
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA {Schema} FROM screenhire_app;
            REVOKE USAGE ON SCHEMA {Schema} FROM screenhire_app;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(GrantMessagingAccess);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RevokeMessagingAccess);
        }
    }
}
