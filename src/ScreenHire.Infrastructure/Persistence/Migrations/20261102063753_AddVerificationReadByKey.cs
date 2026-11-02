using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationReadByKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "verification_review_actions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "api_key_id",
                table: "verification_review_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_verification_review_actions_api_key_id",
                table: "verification_review_actions",
                column: "api_key_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_verification_review_actions_one_actor",
                table: "verification_review_actions",
                sql: "(actor_user_id IS NOT NULL) <> (api_key_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_verification_review_actions_api_keys_api_key_id",
                table: "verification_review_actions",
                column: "api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reads through a key have no person, which the old column cannot hold. The check constraint makes the rows
            // with no actor exactly the rows with a key, so this leaves none for the NOT NULL below to refuse. Migrations
            // run as the table's owner, so unlike the application role they may delete from this append-only table.
            // The table forces row-level security even on its owner, and no tenant is set here, so the policy is lifted
            // for the delete, as RenameIntegrityToVerification does for its data changes. The scope leaves the keys the same
            // way: scopes are stored by name, and the previous release cannot parse this one, so a key left holding it could
            // not be loaded. The key itself stays, with the access it had without the scope.
            migrationBuilder.Sql(
                "ALTER TABLE verification_review_actions NO FORCE ROW LEVEL SECURITY;"
                + " DELETE FROM verification_review_actions WHERE api_key_id IS NOT NULL;"
                + " ALTER TABLE verification_review_actions FORCE ROW LEVEL SECURITY;"
                + " ALTER TABLE api_keys NO FORCE ROW LEVEL SECURITY;"
                + " UPDATE api_keys SET scopes = array_remove(scopes, 'VerificationRead');"
                + " ALTER TABLE api_keys FORCE ROW LEVEL SECURITY;");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_review_actions_api_keys_api_key_id",
                table: "verification_review_actions");

            migrationBuilder.DropIndex(
                name: "ix_verification_review_actions_api_key_id",
                table: "verification_review_actions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_verification_review_actions_one_actor",
                table: "verification_review_actions");

            migrationBuilder.DropColumn(
                name: "api_key_id",
                table: "verification_review_actions");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "verification_review_actions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
