using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Candidate replies (roadmap item 1, ADR-0021), bounce and complaint flags on candidates, and an organization's
    /// sending domain. A reply and its attachments belong to an application, so they get the job-access policy as
    /// <c>application_emails</c> does (ADR-0015); the sending domain is the organization's alone.
    /// </summary>
    public partial class AddRepliesBouncesAndSendingDomains : Migration
    {
        // Row-level security like every tenant table. The application role keeps UPDATE here: a reply's notified_at, an
        // attachment's scan result and the sending domain's check results are all written after the insert.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE inbound_emails ENABLE ROW LEVEL SECURITY;
            ALTER TABLE inbound_emails FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON inbound_emails
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON inbound_emails AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            ALTER TABLE inbound_email_attachments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE inbound_email_attachments FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON inbound_email_attachments
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY job_scope ON inbound_email_attachments AS RESTRICTIVE FOR ALL TO screenhire_app
                USING (app_sees_application(organization_id, application_id))
                WITH CHECK (app_sees_application(organization_id, application_id));
            ALTER TABLE sending_domains ENABLE ROW LEVEL SECURITY;
            ALTER TABLE sending_domains FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON sending_domains
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON sending_domains;
            DROP POLICY IF EXISTS job_scope ON inbound_email_attachments;
            DROP POLICY IF EXISTS tenant_isolation ON inbound_email_attachments;
            DROP POLICY IF EXISTS job_scope ON inbound_emails;
            DROP POLICY IF EXISTS tenant_isolation ON inbound_emails;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_issue",
                table: "candidates",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_issue_at",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_issue_reason",
                table: "candidates",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbound_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    send_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    subject = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    text_body = table.Column<string>(type: "text", nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    auto_reply = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_emails", x => x.id);
                    table.UniqueConstraint("ak_inbound_emails_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_inbound_emails_applications_organization_id_application_id",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sending_domains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    provider_domain_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    records = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sending_domains", x => x.id);
                    table.ForeignKey(
                        name: "fk_sending_domains_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inbound_email_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbound_email_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blob_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_email_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbound_email_attachments_applications_organization_id_appl",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inbound_email_attachments_inbound_emails_organization_id_in",
                        columns: x => new { x.organization_id, x.inbound_email_id },
                        principalTable: "inbound_emails",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_email_attachments_organization_id_application_id",
                table: "inbound_email_attachments",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_email_attachments_organization_id_inbound_email_id",
                table: "inbound_email_attachments",
                columns: new[] { "organization_id", "inbound_email_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_emails_organization_id_application_id",
                table: "inbound_emails",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_emails_organization_id_message_id",
                table: "inbound_emails",
                columns: new[] { "organization_id", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sending_domains_domain",
                table: "sending_domains",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sending_domains_organization_id",
                table: "sending_domains",
                column: "organization_id",
                unique: true);

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "inbound_email_attachments");

            migrationBuilder.DropTable(
                name: "sending_domains");

            migrationBuilder.DropTable(
                name: "inbound_emails");

            migrationBuilder.DropColumn(
                name: "email_issue",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "email_issue_at",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "email_issue_reason",
                table: "candidates");
        }
    }
}
