using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAndRemoveLevel1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ArchiveLevel1Sql);

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_ProjectModerations_EvidenceModerationI~",
                table: "ProjectVerifications");

            migrationBuilder.DropTable(
                name: "EvidenceModerationAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId_EvidenceModeratio~",
                table: "ProjectVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_EvidencePurpose",
                table: "ProjectVerifications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectModerations_Id_Purpose",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_Version",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_Purpose_Status_NextRetryAt",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_EvidenceSnapshot",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Purpose",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "EvidenceModerationId",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "EvidenceModerationPurpose",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "NextRetryAt",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "PolicyHash",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "PolicyJson",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "SnapshotJson",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "ProjectModerations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ProjectModerations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Purpose",
                table: "ProjectModerations",
                sql: "\"Purpose\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ValidateLevel1ArchiveSql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Purpose",
                table: "ProjectModerations");

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceModerationId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "EvidenceModerationPurpose",
                table: "ProjectVerifications",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ProjectModerations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "ProjectModerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "ProjectModerations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "ProjectModerations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "ProjectModerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "ProjectModerations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextRetryAt",
                table: "ProjectModerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyHash",
                table: "ProjectModerations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyJson",
                table: "ProjectModerations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotJson",
                table: "ProjectModerations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceHash",
                table: "ProjectModerations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "ProjectModerations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupersededAt",
                table: "ProjectModerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ProjectModerations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectModerations_Id_Purpose",
                table: "ProjectModerations",
                columns: new[] { "Id", "Purpose" });

            migrationBuilder.CreateTable(
                name: "EvidenceModerationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModerationPurpose = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ResultsJson = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Transient = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceModerationAttempts", x => x.Id);
                    table.CheckConstraint("CK_EvidenceModerationAttempts_Purpose", "\"ModerationPurpose\" = 1");
                    table.ForeignKey(
                        name: "FK_EvidenceModerationAttempts_ProjectModerations_ModerationId_~",
                        columns: x => new { x.ModerationId, x.ModerationPurpose },
                        principalTable: "ProjectModerations",
                        principalColumns: new[] { "Id", "Purpose" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId_EvidenceModeratio~",
                table: "ProjectVerifications",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectVerifications_EvidencePurpose",
                table: "ProjectVerifications",
                sql: "\"EvidenceModerationPurpose\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId",
                table: "ProjectModerations",
                column: "ProjectId",
                unique: true,
                filter: "\"Purpose\" = 1 AND \"SupersededAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_Version",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "Version" },
                unique: true,
                filter: "\"Purpose\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_Purpose_Status_NextRetryAt",
                table: "ProjectModerations",
                columns: new[] { "Purpose", "Status", "NextRetryAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_EvidenceSnapshot",
                table: "ProjectModerations",
                sql: "\"Purpose\" = 0 OR (\"Version\" > 0 AND \"SourceHash\" IS NOT NULL AND \"SnapshotJson\" IS NOT NULL AND \"PolicyHash\" IS NOT NULL AND \"PolicyJson\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Purpose",
                table: "ProjectModerations",
                sql: "\"Purpose\" IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceModerationAttempts_ModerationId_ModerationPurpose",
                table: "EvidenceModerationAttempts",
                columns: new[] { "ModerationId", "ModerationPurpose" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceModerationAttempts_ModerationId_Number",
                table: "EvidenceModerationAttempts",
                columns: new[] { "ModerationId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_ProjectModerations_EvidenceModerationI~",
                table: "ProjectVerifications",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" },
                principalTable: "ProjectModerations",
                principalColumns: new[] { "Id", "Purpose" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(RestoreLevel1Sql);
        }
    }
}
