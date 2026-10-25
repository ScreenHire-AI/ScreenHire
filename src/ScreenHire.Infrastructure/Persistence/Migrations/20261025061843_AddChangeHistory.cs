using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeHistory : Migration
    {
        // Tenant isolation for the change history (CLAUDE.md non-negotiable 6), as in jobs. It is append-only, like
        // membership_events: screenhire_app may read and insert, never change or delete a record.
        private const string EnableRowLevelSecurity = """
            ALTER TABLE change_records ENABLE ROW LEVEL SECURITY;
            ALTER TABLE change_records FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON change_records
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            REVOKE UPDATE, DELETE ON change_records FROM screenhire_app;
            """;

        private const string DisableRowLevelSecurity = """
            DROP POLICY IF EXISTS tenant_isolation ON change_records;
            ALTER TABLE change_records NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE change_records DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No foreign key: the source may be a system template, and the column only names it for the history.
            migrationBuilder.AddColumn<Guid>(
                name: "copied_from_template_id",
                table: "hiring_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "change_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    subject_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_records_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_records_organization_id_subject_kind_subject_id_sequ",
                table: "change_records",
                columns: new[] { "organization_id", "subject_kind", "subject_id", "sequence" },
                descending: new[] { false, false, false, true });

            migrationBuilder.Sql(EnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DisableRowLevelSecurity);

            migrationBuilder.DropTable(
                name: "change_records");

            migrationBuilder.DropColumn(
                name: "copied_from_template_id",
                table: "hiring_templates");
        }
    }
}
