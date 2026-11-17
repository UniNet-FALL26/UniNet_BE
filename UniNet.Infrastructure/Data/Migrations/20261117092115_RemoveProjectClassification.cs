using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Internal creators were always reported as leaders. Preserve that real
            // role before removing the source classification, without changing imports.
            migrationBuilder.Sql("UPDATE \"Projects\" SET \"SubmitterIsLeader\" = TRUE WHERE \"Origin\" = 0;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Origin",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Removed project classifications cannot be reconstructed. Restore a database backup to roll back.");
        }
    }
}
