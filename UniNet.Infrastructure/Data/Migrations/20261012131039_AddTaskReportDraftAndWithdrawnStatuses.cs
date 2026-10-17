using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReportDraftAndWithdrawnStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskReports_Status",
                table: "TaskReports");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskReports_Status",
                table: "TaskReports",
                sql: "\"Status\" BETWEEN 0 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskReports_Status",
                table: "TaskReports");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskReports_Status",
                table: "TaskReports",
                sql: "\"Status\" BETWEEN 0 AND 2");
        }
    }
}
