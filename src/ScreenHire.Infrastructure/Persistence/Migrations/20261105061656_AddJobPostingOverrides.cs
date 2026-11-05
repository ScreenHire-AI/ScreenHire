using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPostingOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "customized_at",
                table: "job_postings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_base",
                table: "job_postings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_override",
                table: "job_postings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title_base",
                table: "job_postings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title_override",
                table: "job_postings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customized_at",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "description_base",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "description_override",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "title_base",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "title_override",
                table: "job_postings");
        }
    }
}
