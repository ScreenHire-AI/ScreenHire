using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitWebhookSecretFromEndpoint : Migration
    {
        // The inputs of an endpoint's signing secret (its version and when it last rotated) move to a row of their own, so
        // change history can track the endpoint (ADR-0016). Every secret keeps its version: a reset would change what
        // receivers are sent signed with. The new table is tenant-owned, with the same policy as every other.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE webhook_endpoint_secrets ENABLE ROW LEVEL SECURITY;
            ALTER TABLE webhook_endpoint_secrets FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON webhook_endpoint_secrets
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // Both tables are forced, so with no tenant set the owner reads and writes nothing unless the force is lifted around
        // the copy. It is put back before the migration ends, and the copy aborts it when a row was missed.
        private const string CopyIntoSecrets = """
            ALTER TABLE webhook_endpoints NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE webhook_endpoint_secrets NO FORCE ROW LEVEL SECURITY;
            INSERT INTO webhook_endpoint_secrets (endpoint_id, organization_id, version, rotated_at)
                SELECT id, organization_id, secret_version, secret_rotated_at FROM webhook_endpoints;
            DO $$
            BEGIN
                IF (SELECT count(*) FROM webhook_endpoint_secrets) <> (SELECT count(*) FROM webhook_endpoints) THEN
                    RAISE EXCEPTION 'The webhook secrets were not all copied.';
                END IF;
            END $$;
            ALTER TABLE webhook_endpoint_secrets FORCE ROW LEVEL SECURITY;
            ALTER TABLE webhook_endpoints FORCE ROW LEVEL SECURITY;
            """;

        private const string CopyBackToEndpoints = """
            ALTER TABLE webhook_endpoints NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE webhook_endpoint_secrets NO FORCE ROW LEVEL SECURITY;
            UPDATE webhook_endpoints e SET secret_version = s.version, secret_rotated_at = s.rotated_at
                FROM webhook_endpoint_secrets s WHERE s.endpoint_id = e.id;
            ALTER TABLE webhook_endpoints FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_endpoint_secrets",
                columns: table => new
                {
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoint_secrets", x => x.endpoint_id);
                    table.ForeignKey(
                        name: "fk_webhook_endpoint_secrets_webhook_endpoints_organization_id_",
                        columns: x => new { x.organization_id, x.endpoint_id },
                        principalTable: "webhook_endpoints",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoint_secrets_organization_id_endpoint_id",
                table: "webhook_endpoint_secrets",
                columns: new[] { "organization_id", "endpoint_id" },
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
            migrationBuilder.Sql(CopyIntoSecrets);

            migrationBuilder.DropColumn(
                name: "secret_rotated_at",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "secret_version",
                table: "webhook_endpoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "secret_rotated_at",
                table: "webhook_endpoints",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "secret_version",
                table: "webhook_endpoints",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(CopyBackToEndpoints);

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON webhook_endpoint_secrets;
                ALTER TABLE webhook_endpoint_secrets NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE webhook_endpoint_secrets DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "webhook_endpoint_secrets");
        }
    }
}
