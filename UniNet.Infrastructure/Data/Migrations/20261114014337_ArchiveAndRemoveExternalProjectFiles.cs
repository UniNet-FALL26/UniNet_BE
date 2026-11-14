using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAndRemoveExternalProjectFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '15s';
                LOCK TABLE "ExternalProjectDrafts", "ExternalProjectDraftFiles", "ProjectEvidenceDocuments"
                    IN ACCESS EXCLUSIVE MODE;

                CREATE SCHEMA external_project_file_archive;
                REVOKE ALL ON SCHEMA external_project_file_archive FROM PUBLIC;
                CREATE TABLE external_project_file_archive."ExternalProjectDraftFiles" AS
                    SELECT "DraftId", "UploadedFileId" FROM "ExternalProjectDraftFiles";
                CREATE TABLE external_project_file_archive."ProjectEvidenceDocuments" AS
                    SELECT "Id", "ProjectId", "UploadedFileId", "CreatedAt" FROM "ProjectEvidenceDocuments";

                UPDATE "ExternalProjectDrafts"
                SET "Status" = CASE WHEN "ExpiresAt" <= CURRENT_TIMESTAMP THEN 4 ELSE 1 END,
                    "ErrorCode" = NULL, "Version" = "Version" + 1, "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE "Status" IN (0, 2);
                """);

            migrationBuilder.DropTable(
                name: "ExternalProjectDraftFiles");

            migrationBuilder.DropTable(
                name: "ProjectEvidenceDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ExternalProjectDrafts_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ExternalProjectDrafts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ExternalProjectDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "ExternalProjectDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "ExternalProjectDrafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "ExternalProjectDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalProjectDraftFiles",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedFileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalProjectDraftFiles", x => new { x.DraftId, x.UploadedFileId });
                    table.ForeignKey(
                        name: "FK_ExternalProjectDraftFiles_ExternalProjectDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "ExternalProjectDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalProjectDraftFiles_UploadedFiles_UploadedFileId",
                        column: x => x.UploadedFileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectEvidenceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEvidenceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceDocuments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceDocuments_UploadedFiles_UploadedFileId",
                        column: x => x.UploadedFileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDrafts_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ExternalProjectDrafts",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDraftFiles_UploadedFileId",
                table: "ExternalProjectDraftFiles",
                column: "UploadedFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceDocuments_ProjectId_UploadedFileId",
                table: "ProjectEvidenceDocuments",
                columns: new[] { "ProjectId", "UploadedFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceDocuments_UploadedFileId",
                table: "ProjectEvidenceDocuments",
                column: "UploadedFileId");

            migrationBuilder.Sql("""
                INSERT INTO "ExternalProjectDraftFiles" ("DraftId", "UploadedFileId")
                    SELECT "DraftId", "UploadedFileId"
                    FROM external_project_file_archive."ExternalProjectDraftFiles";
                INSERT INTO "ProjectEvidenceDocuments" ("Id", "ProjectId", "UploadedFileId", "CreatedAt")
                    SELECT "Id", "ProjectId", "UploadedFileId", "CreatedAt"
                    FROM external_project_file_archive."ProjectEvidenceDocuments";
                DROP TABLE external_project_file_archive."ExternalProjectDraftFiles";
                DROP TABLE external_project_file_archive."ProjectEvidenceDocuments";
                DROP SCHEMA external_project_file_archive;
                """);
        }
    }
}
