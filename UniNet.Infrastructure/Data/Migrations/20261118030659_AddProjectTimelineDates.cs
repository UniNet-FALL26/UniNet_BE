using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTimelineDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ActualEndDate",
                table: "ProjectDetails",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StartDateIsCreationDate",
                table: "ProjectDetails",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualEndDate",
                table: "ProjectDetails");

            migrationBuilder.DropColumn(
                name: "StartDateIsCreationDate",
                table: "ProjectDetails");
        }
    }
}
