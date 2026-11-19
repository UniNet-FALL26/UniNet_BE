using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectExpectedEndDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The refactor copied confirmed import completion dates into EndDate
            // before ActualEndDate existed. Preserve only exact source matches.
            migrationBuilder.Sql("""
                UPDATE "ProjectDetails" AS d
                SET "ActualEndDate" = d."EndDate"
                FROM "Projects" AS p
                WHERE d."ProjectId" = p."Id"
                  AND d."ActualEndDate" IS NULL
                  AND d."EndDate" IS NOT NULL
                  AND p."ImplementationEndDate" = to_char(d."EndDate", 'YYYY-MM-DD');
                """);
            migrationBuilder.DropColumn(
                name: "ExpectedOutput",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "ProjectDetails");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpectedOutput",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "ProjectDetails",
                type: "date",
                nullable: true);
        }
    }
}
