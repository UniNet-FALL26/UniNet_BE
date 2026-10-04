using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_Status_RecruitmentStatus",
                table: "Projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt",
                table: "ProjectModerations");

            migrationBuilder.AddColumn<short>(
                name: "Visibility",
                table: "Projects",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                table: "ProjectModerations",
                type: "numeric(4,3)",
                precision: 4,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "ProjectModerations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "ProjectModerations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "ProjectModerations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ResolvedUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CheckResult = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_Projects_Visibility_Status_RecruitmentStatus",
                table: "Projects",
                columns: new[] { "Visibility", "Status", "RecruitmentStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Visibility",
                table: "Projects",
                sql: "\"Visibility\" BETWEEN 0 AND 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt_ContentHash",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "CreatedAt", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ReviewedByUserId",
                table: "ProjectModerations",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLinks_ProjectId_NormalizedUrl",
                table: "ProjectLinks",
                columns: new[] { "ProjectId", "NormalizedUrl" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectModerations_Users_ReviewedByUserId",
                table: "ProjectModerations",
                column: "ReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectModerations_Users_ReviewedByUserId",
                table: "ProjectModerations");

            migrationBuilder.DropTable(
                name: "ProjectLinks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_Visibility_Status_RecruitmentStatus",
                table: "Projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Visibility",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt_ContentHash",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ReviewedByUserId",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "ProjectModerations");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Status_RecruitmentStatus",
                table: "Projects",
                columns: new[] { "Status", "RecruitmentStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects",
                sql: "\"Status\" BETWEEN 0 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_CreatedAt",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "CreatedAt" });
        }
    }
}
