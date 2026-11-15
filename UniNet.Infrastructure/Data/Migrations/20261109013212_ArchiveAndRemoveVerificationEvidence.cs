using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAndRemoveVerificationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ArchiveEvidenceSql);
            migrationBuilder.DropTable(
                name: "ProjectVerificationEvidence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ValidateEvidenceArchiveSql);
            migrationBuilder.CreateTable(
                name: "ProjectVerificationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVerificationEvidence", x => x.Id);
                    table.CheckConstraint("CK_ProjectVerificationEvidence_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ProjectVerificationEvidence_ProjectVerifications_ProjectVer~",
                        column: x => x.ProjectVerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectVerificationEvidence_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerificationEvidence_ProjectVerificationId_Version",
                table: "ProjectVerificationEvidence",
                columns: new[] { "ProjectVerificationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerificationEvidence_SubmittedByUserId",
                table: "ProjectVerificationEvidence",
                column: "SubmittedByUserId");

            migrationBuilder.Sql(RestoreEvidenceSql);
        }
    }
}
