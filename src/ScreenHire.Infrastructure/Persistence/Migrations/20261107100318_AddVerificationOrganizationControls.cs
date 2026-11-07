using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationOrganizationControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "budget_alert_email",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "budget_alert_month",
                table: "verification_settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "budget_alert_slack",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "check_answers",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "check_contact",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "check_documents",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "check_duplicates",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "check_form_use",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "check_network",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "default_ai_use",
                table: "verification_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AllowedWithDisclosure");

            migrationBuilder.AddColumn<string>(
                name: "device_check",
                table: "verification_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "llm_base_url",
                table: "verification_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "llm_key_version",
                table: "verification_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "llm_model",
                table: "verification_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "llm_provider",
                table: "verification_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "llm_source",
                table: "verification_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Included");

            migrationBuilder.AddColumn<string>(
                name: "protected_llm_api_key",
                table: "verification_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "suggested_alert_email",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "suggested_alert_slack",
                table: "verification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "trusted_domains",
                table: "verification_settings",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string[]>(
                name: "trusted_networks",
                table: "verification_settings",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_alert_email",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "budget_alert_month",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "budget_alert_slack",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "check_answers",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "check_contact",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "check_documents",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "check_duplicates",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "check_form_use",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "check_network",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "default_ai_use",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "device_check",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "llm_base_url",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "llm_key_version",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "llm_model",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "llm_provider",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "llm_source",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "protected_llm_api_key",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "suggested_alert_email",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "suggested_alert_slack",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "trusted_domains",
                table: "verification_settings");

            migrationBuilder.DropColumn(
                name: "trusted_networks",
                table: "verification_settings");
        }
    }
}
