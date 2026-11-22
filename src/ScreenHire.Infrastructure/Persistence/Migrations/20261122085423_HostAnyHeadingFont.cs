using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HostAnyHeadingFont : Migration
    {
        // The column held one of six enum names (ADR-0018); it holds a Fontsource id and the family beside it since
        // ADR-0031. Every stored name maps to the same font, so nobody's choice changes. organizations forces row-level
        // security, which binds the owner too, so the update lifts it for this transaction only (as AddHiringModel's
        // backfill does); a failure rolls it all back.
        private const string MapEnumNamesToIds = """
            ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;
            UPDATE organizations SET
                heading_font = CASE heading_font
                    WHEN 'Inter' THEN 'inter'
                    WHEN 'Montserrat' THEN 'montserrat'
                    WHEN 'Nunito' THEN 'nunito'
                    WHEN 'Fraunces' THEN 'fraunces'
                    WHEN 'PlayfairDisplay' THEN 'playfair-display'
                END,
                heading_font_family = CASE heading_font
                    WHEN 'Inter' THEN 'Inter'
                    WHEN 'Montserrat' THEN 'Montserrat'
                    WHEN 'Nunito' THEN 'Nunito'
                    WHEN 'Fraunces' THEN 'Fraunces'
                    WHEN 'PlayfairDisplay' THEN 'Playfair Display'
                END
            WHERE heading_font IS NOT NULL;
            ALTER TABLE organizations FORCE ROW LEVEL SECURITY;
            """;

        // Going back keeps the six that were enum names and returns everyone else to ScreenHire's font.
        private const string MapIdsToEnumNames = """
            ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;
            UPDATE organizations SET heading_font = CASE heading_font
                WHEN 'inter' THEN 'Inter'
                WHEN 'montserrat' THEN 'Montserrat'
                WHEN 'nunito' THEN 'Nunito'
                WHEN 'fraunces' THEN 'Fraunces'
                WHEN 'playfair-display' THEN 'PlayfairDisplay'
            END
            WHERE heading_font IS NOT NULL;
            ALTER TABLE organizations FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "heading_font",
                table: "organizations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "heading_font_family",
                table: "organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(MapEnumNamesToIds);

            migrationBuilder.CreateTable(
                name: "hosted_fonts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    family = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    license = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    files = table.Column<string>(type: "jsonb", nullable: false),
                    hosted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hosted_fonts", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hosted_fonts");

            migrationBuilder.Sql(MapIdsToEnumNames);

            migrationBuilder.DropColumn(
                name: "heading_font_family",
                table: "organizations");

            migrationBuilder.AlterColumn<string>(
                name: "heading_font",
                table: "organizations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
