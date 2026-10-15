using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowUncategorizedJobSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Skills_Category",
                table: "Skills");

            migrationBuilder.AlterColumn<short>(
                name: "Category",
                table: "Skills",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Skills_Category",
                table: "Skills",
                sql: "\"Category\" IS NULL OR \"Category\" BETWEEN 0 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Skills_Category",
                table: "Skills");

            migrationBuilder.AlterColumn<short>(
                name: "Category",
                table: "Skills",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Skills_Category",
                table: "Skills",
                sql: "\"Category\" BETWEEN 0 AND 3");
        }
    }
}
