using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifySkillCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Skills_Category_IsActive_DisplayOrder",
                table: "Skills");

            migrationBuilder.DropIndex(
                name: "IX_Skills_Slug",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Skills");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Category_IsActive",
                table: "Skills",
                columns: new[] { "Category", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Skills_Category_IsActive",
                table: "Skills");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Skills",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Skills",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Skills",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Original text values need the pre-migration backup; IDs keep this fallback unique.
            migrationBuilder.Sql("UPDATE \"Skills\" SET \"Slug\" = 'skill-' || \"Id\"::text;");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Category_IsActive_DisplayOrder",
                table: "Skills",
                columns: new[] { "Category", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Slug",
                table: "Skills",
                column: "Slug",
                unique: true);
        }
    }
}
