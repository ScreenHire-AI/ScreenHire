using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberPreferences : Migration
    {
        // Tenant row-level security like every tenant table (AddSignInAndTenantIsolation). Digest items belong to one person,
        // but the worker writes and sends them for people it does not act as, so the policy is the organization's and every
        // read narrows to the person, as for notifications. user_photos is global like users and has none.
        // A digest item names the job and the application it is about, so it also gets a restrictive job_scope policy
        // (ADR-0015, AddJobAccess): a member on Only jobs they're on sees none about another job. Both ids can be null (an AI
        // use item names neither), which every member passes. The worker sets no user, so it sees every row.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE member_preferences ENABLE ROW LEVEL SECURITY;
            ALTER TABLE member_preferences FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON member_preferences
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ALTER TABLE digest_items ENABLE ROW LEVEL SECURITY;
            ALTER TABLE digest_items FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON digest_items
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON digest_items AS RESTRICTIVE FOR ALL TO screenhire_app
                USING ((job_id IS NULL OR app_sees_job(organization_id, job_id))
                    AND (application_id IS NULL OR app_sees_application(organization_id, application_id)))
                WITH CHECK ((job_id IS NULL OR app_sees_job(organization_id, job_id))
                    AND (application_id IS NULL OR app_sees_application(organization_id, application_id)));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "digest_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digest_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_digest_items_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_digest_items_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "member_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    date_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shortcuts_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    confirm_moves = table.Column<bool>(type: "boolean", nullable: true),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notices = table.Column<string>(type: "jsonb", nullable: false),
                    last_digest_on = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_preferences", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_preferences_organization_members_member_id",
                        column: x => x.member_id,
                        principalTable: "organization_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_member_preferences_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_member_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_photos",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_photos", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_photos_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_digest_items_organization_id_user_id_kind_source_id",
                table: "digest_items",
                columns: new[] { "organization_id", "user_id", "kind", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_digest_items_organization_id_user_id_sent_at",
                table: "digest_items",
                columns: new[] { "organization_id", "user_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_digest_items_user_id",
                table: "digest_items",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_preferences_member_id",
                table: "member_preferences",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_preferences_organization_id_member_id",
                table: "member_preferences",
                columns: new[] { "organization_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_member_preferences_organization_id_user_id",
                table: "member_preferences",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_member_preferences_user_id",
                table: "member_preferences",
                column: "user_id");

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "digest_items");

            migrationBuilder.DropTable(
                name: "member_preferences");

            migrationBuilder.DropTable(
                name: "user_photos");
        }
    }
}
