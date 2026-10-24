using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMinimumQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinimumQuantity",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE \"Projects\" SET \"MinimumQuantity\" = \"MemberTarget\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_MinimumQuantity",
                table: "Projects",
                sql: "\"MinimumQuantity\" >= 1 AND \"MinimumQuantity\" <= \"MemberTarget\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_MinimumQuantity",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "MinimumQuantity",
                table: "Projects");
        }
    }
}
