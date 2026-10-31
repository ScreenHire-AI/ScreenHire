using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Branding v1 (ADR-0018, docs/architecture/branding.md): the organization's second colour, heading font and the
    /// brand images in use, and <c>brand_images</c> for the uploads behind them. The three image columns are not foreign
    /// keys: Postgres checks a foreign key without row-level security, so a key would accept another organization's
    /// image, and the branding save looks each one up within the tenant instead.
    /// </summary>
    public partial class AddBrandingImagesAndFonts : Migration
    {
        private const string Upgrade = """
            ALTER TABLE brand_images ENABLE ROW LEVEL SECURITY;
            ALTER TABLE brand_images FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON brand_images
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        private const string Downgrade = """
            DROP POLICY IF EXISTS tenant_isolation ON brand_images;
            ALTER TABLE brand_images NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE brand_images DISABLE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "favicon_image_id",
                table: "organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "heading_font",
                table: "organizations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_image_id",
                table: "organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "second_color",
                table: "organizations",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sharing_image_id",
                table: "organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "brand_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    blob_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_brand_images_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brand_images_organization_id_kind",
                table: "brand_images",
                columns: new[] { "organization_id", "kind" });

            migrationBuilder.Sql(Upgrade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Downgrade);

            migrationBuilder.DropTable(
                name: "brand_images");

            migrationBuilder.DropColumn(
                name: "favicon_image_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "heading_font",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "logo_image_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "second_color",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "sharing_image_id",
                table: "organizations");
        }
    }
}
