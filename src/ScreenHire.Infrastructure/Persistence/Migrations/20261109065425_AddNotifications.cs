using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        // Tenant row-level security like every tenant table (AddSignInAndTenantIsolation). A notification belongs to one
        // person, but the worker raises and clears them for others, so the policy is the organization's and the API
        // narrows every read and write to the signed-in user.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
            ALTER TABLE notifications FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON notifications
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verification_lost_at",
                table: "sending_domains",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_organization_id_user_id_created_at",
                table: "notifications",
                columns: new[] { "organization_id", "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_organization_id_user_id_kind_key",
                table: "notifications",
                columns: new[] { "organization_id", "user_id", "kind", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id",
                table: "notifications",
                column: "user_id");

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropColumn(
                name: "verification_lost_at",
                table: "sending_domains");
        }
    }
}
