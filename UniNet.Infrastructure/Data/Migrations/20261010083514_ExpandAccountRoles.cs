using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAccountRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts",
                sql: "\"Role\" BETWEEN 0 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts",
                sql: "\"Role\" BETWEEN 0 AND 2");
        }
    }
}
