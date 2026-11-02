using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationSourceBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_board",
                table: "applications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_board",
                table: "applications");
        }
    }
}
