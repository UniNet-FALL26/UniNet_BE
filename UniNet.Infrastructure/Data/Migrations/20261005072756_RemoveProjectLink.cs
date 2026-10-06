using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectLinks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt_ContentHash",
                table: "ProjectModerations");

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "ProjectModerations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewAt",
                table: "ProjectModerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "ProjectModerations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_AttemptNumber",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_AttemptNumber",
                table: "ProjectModerations",
                sql: "\"AttemptNumber\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Confidence",
                table: "ProjectModerations",
                sql: "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Confidence",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "ReviewAt",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "ProjectModerations");

            migrationBuilder.CreateTable(
                name: "ProjectLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckResult = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NormalizedUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResolvedUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectLinks", x => x.Id);
                    table.CheckConstraint("CK_ProjectLinks_CheckResult", "\"CheckResult\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_ProjectLinks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt_ContentHash",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "CreatedAt", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLinks_ProjectId_NormalizedUrl",
                table: "ProjectLinks",
                columns: new[] { "ProjectId", "NormalizedUrl" },
                unique: true);
        }
    }
}
