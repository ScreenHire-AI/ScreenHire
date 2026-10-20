using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantAppRoleApiMessagingAccess : Migration
    {
        private const string Schema = DatabaseRegistration.ApiMessagingSchema;

        // The API's own Wolverine storage (its outbox, the real-time queue's inbox, its node records), apart from the
        // worker's so the two hosts are separate Wolverine clusters (docs/architecture/realtime.md). The same grants as
        // GrantAppRoleMessagingAccess: the owner creates the tables (ScreenHireMessaging.ProvisionStorageAsync, run after
        // this migration), screenhire_app only reads and writes rows, and the tables hold envelopes, not tenant rows.
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
