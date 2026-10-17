using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReportRevisionGroupsAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskReports_ProjectTaskId_SubmittedById_Version",
                table: "TaskReports");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "TaskReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById",
                table: "TaskReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TaskReports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentReportId",
                table: "TaskReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportGroupId",
                table: "TaskReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "TaskReports" AS child
                SET "ParentReportId" = parent."Id"
                FROM "TaskReports" AS parent
                WHERE child."ProjectTaskId" = parent."ProjectTaskId"
                  AND child."SubmittedById" = parent."SubmittedById"
                  AND child."Version" = parent."Version" + 1
                  AND parent."Status" = 2;

                WITH RECURSIVE "ReportLineage" ("ReportId", "RootId") AS
                (
                    SELECT "Id", "Id"
                    FROM "TaskReports"
                    WHERE "ParentReportId" IS NULL
                    UNION ALL
                    SELECT child."Id", lineage."RootId"
                    FROM "TaskReports" AS child
                    JOIN "ReportLineage" AS lineage ON child."ParentReportId" = lineage."ReportId"
                )
                UPDATE "TaskReports" AS report
                SET "ReportGroupId" = lineage."RootId"
                FROM "ReportLineage" AS lineage
                WHERE report."Id" = lineage."ReportId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReportGroupId",
                table: "TaskReports",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_DeletedById",
                table: "TaskReports",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_ParentReportId",
                table: "TaskReports",
                column: "ParentReportId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_ReportGroupId_Version",
                table: "TaskReports",
                columns: new[] { "ReportGroupId", "Version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskReports_DeletedMetadata",
                table: "TaskReports",
                sql: "(\"IsDeleted\" = FALSE AND \"DeletedAt\" IS NULL AND \"DeletedById\" IS NULL) OR (\"IsDeleted\" = TRUE AND \"DeletedAt\" IS NOT NULL AND \"DeletedById\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskReports_TaskReports_ParentReportId",
                table: "TaskReports",
                column: "ParentReportId",
                principalTable: "TaskReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskReports_Users_DeletedById",
                table: "TaskReports",
                column: "DeletedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskReports_TaskReports_ParentReportId",
                table: "TaskReports");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskReports_Users_DeletedById",
                table: "TaskReports");

            migrationBuilder.DropIndex(
                name: "IX_TaskReports_DeletedById",
                table: "TaskReports");

            migrationBuilder.DropIndex(
                name: "IX_TaskReports_ParentReportId",
                table: "TaskReports");

            migrationBuilder.DropIndex(
                name: "IX_TaskReports_ReportGroupId_Version",
                table: "TaskReports");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskReports_DeletedMetadata",
                table: "TaskReports");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TaskReports");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "TaskReports");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TaskReports");

            migrationBuilder.DropColumn(
                name: "ParentReportId",
                table: "TaskReports");

            migrationBuilder.DropColumn(
                name: "ReportGroupId",
                table: "TaskReports");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_ProjectTaskId_SubmittedById_Version",
                table: "TaskReports",
                columns: new[] { "ProjectTaskId", "SubmittedById", "Version" },
                unique: true);
        }
    }
}
