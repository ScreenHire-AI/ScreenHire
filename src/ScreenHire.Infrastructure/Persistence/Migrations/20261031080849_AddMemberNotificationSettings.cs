using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notifications",
                table: "organization_members",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{\"toasts\":true,\"closeToasts\":true,\"off\":[]}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notifications",
                table: "organization_members");
        }
    }
}
