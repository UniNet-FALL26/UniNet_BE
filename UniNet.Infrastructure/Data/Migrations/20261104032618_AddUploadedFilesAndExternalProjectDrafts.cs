using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedFilesAndExternalProjectDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalProjectDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ExtractedDataJson = table.Column<string>(type: "jsonb", nullable: true),
                    EditedDataJson = table.Column<string>(type: "jsonb", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalProjectDrafts", x => x.Id);
                    table.CheckConstraint("CK_ExternalProjectDrafts_Confirmation", "(\"Status\" = 3 AND \"ConfirmedProjectId\" IS NOT NULL) OR (\"Status\" <> 3 AND \"ConfirmedProjectId\" IS NULL)");
                    table.CheckConstraint("CK_ExternalProjectDrafts_Status", "\"Status\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_ExternalProjectDrafts_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ExternalProjectDrafts_Projects_ConfirmedProjectId",
                        column: x => x.ConfirmedProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalProjectDrafts_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UploadedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedFiles", x => x.Id);
                    table.CheckConstraint("CK_UploadedFiles_ResourceType", "\"ResourceType\" IN ('image', 'raw', 'video')");
                    table.CheckConstraint("CK_UploadedFiles_SizeBytes", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_UploadedFiles_Accounts_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDraftFiles_UploadedFileId",
                table: "ExternalProjectDraftFiles",
                column: "UploadedFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDrafts_ConfirmedProjectId",
                table: "ExternalProjectDrafts",
                column: "ConfirmedProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDrafts_OwnerUserId_Status_CreatedAt",
                table: "ExternalProjectDrafts",
                columns: new[] { "OwnerUserId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDrafts_Status_ExpiresAt",
                table: "ExternalProjectDrafts",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_OwnerAccountId_CreatedAt",
                table: "UploadedFiles",
                columns: new[] { "OwnerAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_ResourceType_PublicId",
                table: "UploadedFiles",
                columns: new[] { "ResourceType", "PublicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalProjectDraftFiles");

            migrationBuilder.DropTable(
                name: "ExternalProjectDrafts");

            migrationBuilder.DropTable(
                name: "UploadedFiles");
        }
    }
}
