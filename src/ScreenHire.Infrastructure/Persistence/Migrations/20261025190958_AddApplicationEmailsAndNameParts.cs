using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The email composer and first and last names (roadmap item 2). <c>application_emails</c> keeps each email to a
    /// candidate as it went, so Messages shows it after its template changes; the <c>EmailSent</c> timeline entry names
    /// it by id. Candidates get <c>first_name</c> and <c>last_name</c>; <c>full_name</c> stays, derived from them, and
    /// widens to hold both.
    /// </summary>
    public partial class AddApplicationEmailsAndNameParts : Migration
    {
        // Every candidate so far keeps their whole name as the first name, with no last name: nobody's name is split by
        // guesswork. Row-level security is forced on candidates and its policy matches no row without a tenant, so FORCE is
        // off around the copy (AddApplicationNotes). The column is added nullable, filled, then made required, so no
        // default is left behind.
        private const string KeepNamesWhole = """
            ALTER TABLE candidates NO FORCE ROW LEVEL SECURITY;
            UPDATE candidates SET first_name = full_name;
            ALTER TABLE candidates FORCE ROW LEVEL SECURITY;
            """;

        // Row-level security like every tenant table. A copy is never changed, so the application role loses UPDATE; it
        // keeps DELETE, because the worker removes the copy of an email it decides not to send.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE application_emails ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_emails FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_emails
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE ON application_emails FROM screenhire_app;
            """;

        // Back to one name: a full name longer than the old column is cut to fit.
        private const string FitFullNames = """
            ALTER TABLE candidates NO FORCE ROW LEVEL SECURITY;
            UPDATE candidates SET full_name = left(full_name, 200) WHERE length(full_name) > 200;
            ALTER TABLE candidates FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "candidates",
                type: "character varying(301)",
                maxLength: 301,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(KeepNamesWhole);

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                table: "candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "candidates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "application_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    to = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    text_body = table.Column<string>(type: "text", nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_emails_applications_organization_id_application",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_emails_organization_id_application_id",
                table: "application_emails",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON application_emails;");

            migrationBuilder.DropTable(
                name: "application_emails");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "candidates");

            migrationBuilder.Sql(FitFullNames);

            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "candidates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(301)",
                oldMaxLength: 301);
        }
    }
}
