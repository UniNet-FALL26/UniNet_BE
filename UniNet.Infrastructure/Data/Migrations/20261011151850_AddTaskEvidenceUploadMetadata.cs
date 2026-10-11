using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskEvidenceUploadMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "TaskEvidence",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "TaskEvidence",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TaskEvidence",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "TaskEvidence",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileName",
                table: "TaskEvidence");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "TaskEvidence");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TaskEvidence");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "TaskEvidence");
        }
    }
}
