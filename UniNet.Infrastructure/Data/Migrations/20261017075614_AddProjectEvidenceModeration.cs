using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectEvidenceModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "VerificationId",
                table: "VerificationNotification",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceModerationId",
                table: "VerificationNotification",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectEvidenceModerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    PolicyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEvidenceModerations", x => x.Id);
                    table.CheckConstraint("CK_EvidenceModeration_Status", "\"Status\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_EvidenceModeration_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceModerations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceModerationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Transient = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceModerationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceModerationAttempts_ProjectEvidenceModerations_Moder~",
                        column: x => x.ModerationId,
                        principalTable: "ProjectEvidenceModerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_EvidenceModerationId",
                table: "VerificationNotification",
                column: "EvidenceModerationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification",
                sql: "(\"VerificationId\" IS NOT NULL AND \"EvidenceModerationId\" IS NULL) OR (\"VerificationId\" IS NULL AND \"EvidenceModerationId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceModerationAttempts_ModerationId_Number",
                table: "EvidenceModerationAttempts",
                columns: new[] { "ModerationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceModerations_ProjectId",
                table: "ProjectEvidenceModerations",
                column: "ProjectId",
                unique: true,
                filter: "\"SupersededAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceModerations_ProjectId_Version",
                table: "ProjectEvidenceModerations",
                columns: new[] { "ProjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceModerations_Status_NextRetryAt",
                table: "ProjectEvidenceModerations",
                columns: new[] { "Status", "NextRetryAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_ProjectEvidenceModerations_Evidenc~",
                table: "VerificationNotification",
                column: "EvidenceModerationId",
                principalTable: "ProjectEvidenceModerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // These rows have no level-2 VerificationId in the old schema.
            migrationBuilder.Sql("DELETE FROM \"VerificationNotification\" WHERE \"EvidenceModerationId\" IS NOT NULL;");
            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_ProjectEvidenceModerations_Evidenc~",
                table: "VerificationNotification");

            migrationBuilder.DropTable(
                name: "EvidenceModerationAttempts");

            migrationBuilder.DropTable(
                name: "ProjectEvidenceModerations");

            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_EvidenceModerationId",
                table: "VerificationNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification");

            migrationBuilder.DropColumn(
                name: "EvidenceModerationId",
                table: "VerificationNotification");

            migrationBuilder.AlterColumn<Guid>(
                name: "VerificationId",
                table: "VerificationNotification",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
