using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifyProjectModerationPurposes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
LOCK TABLE "ProjectModerations", "ProjectEvidenceModerations", "ProjectVerifications", "EvidenceModerationAttempts", "VerificationNotification" IN ACCESS EXCLUSIVE MODE;
DO $guard$
BEGIN
    IF EXISTS (SELECT 1 FROM "ProjectModerations" a JOIN "ProjectEvidenceModerations" b ON a."Id" = b."Id") THEN
        RAISE EXCEPTION 'Moderation ID collision: stop and define an explicit ID mapping before G1';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE contype = 'f' AND confrelid = '"ProjectEvidenceModerations"'::regclass
        AND conname NOT IN ('FK_EvidenceModerationAttempts_ProjectEvidenceModerations_Moder~',
            'FK_ProjectVerifications_ProjectEvidenceModerations_EvidenceMod~',
            'FK_VerificationNotification_ProjectEvidenceModerations_Evidenc~')) THEN
        RAISE EXCEPTION 'Unexpected FK targets evidence moderation: inventory and migrate it before G1';
    END IF;
END $guard$;
""");
            migrationBuilder.DropForeignKey(
                name: "FK_EvidenceModerationAttempts_ProjectEvidenceModerations_Moder~",
                table: "EvidenceModerationAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectModerations_Projects_ProjectId",
                table: "ProjectModerations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_ProjectEvidenceModerations_EvidenceMod~",
                table: "ProjectVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_ProjectEvidenceModerations_Evidenc~",
                table: "VerificationNotification");



            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_EvidenceModerationId",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId",
                table: "ProjectVerifications");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations");

            migrationBuilder.AddColumn<short>(
                name: "EvidenceModerationPurpose",
                table: "VerificationNotification",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

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

            migrationBuilder.AddColumn<short>(
                name: "Purpose",
                table: "ProjectModerations",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

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

            migrationBuilder.AddColumn<short>(
                name: "ModerationPurpose",
                table: "EvidenceModerationAttempts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.Sql("""
INSERT INTO "ProjectModerations" ("Id", "ProjectId", "Purpose", "Status", "ContentResult", "LinkResult", "ContentHash", "AttemptNumber", "RetryCount", "CreatedAt", "UpdatedAt",
    "Version", "SourceHash", "SnapshotJson", "PolicyHash", "PolicyJson", "SupersededAt", "CompletedAt", "LeaseExpiresAt", "LeaseToken", "NextRetryAt", "AttemptCount", "LastErrorCode", "LastError", "Summary")
SELECT "Id", "ProjectId", 1, CASE "Status" WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 2 THEN 6 WHEN 3 THEN 4 WHEN 4 THEN 7 WHEN 5 THEN 5 END,
    0, 0, '', 0, 0, "CreatedAt", "CreatedAt", "Version", "SourceHash", "SnapshotJson", "PolicyHash", "PolicyJson", "SupersededAt", "CompletedAt", "LeaseExpiresAt", "LeaseToken", "NextRetryAt", "AttemptCount", "LastErrorCode", "LastError", "Summary"
FROM "ProjectEvidenceModerations";

DO $verify$
BEGIN
    IF (SELECT count(*) FROM "ProjectEvidenceModerations") <> (SELECT count(*) FROM "ProjectModerations" WHERE "Purpose" = 1)
    OR EXISTS (
        SELECT 1 FROM "ProjectEvidenceModerations" old
        FULL JOIN (SELECT "Id", "ProjectId", "Version", "SourceHash", "SnapshotJson", "PolicyHash", "PolicyJson", "CreatedAt",
            CASE "Status" WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 6 THEN 2 WHEN 4 THEN 3 WHEN 7 THEN 4 WHEN 5 THEN 5 END::smallint AS "Status",
            "SupersededAt", "CompletedAt", "LeaseExpiresAt", "LeaseToken", "NextRetryAt", "AttemptCount", "LastErrorCode", "LastError", "Summary"
            FROM "ProjectModerations" WHERE "Purpose" = 1) copied ON old."Id" = copied."Id"
        WHERE to_jsonb(old) IS DISTINCT FROM to_jsonb(copied)
    ) THEN RAISE EXCEPTION 'G1 row/key/snapshot/hash/history comparison failed; old table retained by transaction rollback'; END IF;
END $verify$;
""");
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectModerations_Id_Purpose",
                table: "ProjectModerations",
                columns: new[] { "Id", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_EvidenceModerationId_EvidenceModer~",
                table: "VerificationNotification",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationNotification_EvidencePurpose",
                table: "VerificationNotification",
                sql: "\"EvidenceModerationPurpose\" = 1");

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
                name: "IX_ProjectModerations_ProjectId_AttemptNumber",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "AttemptNumber" },
                unique: true,
                filter: "\"Purpose\" = 0");

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
                name: "CK_ProjectModerations_AttemptNumber",
                table: "ProjectModerations",
                sql: "\"Purpose\" = 1 OR \"AttemptNumber\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_EvidenceSnapshot",
                table: "ProjectModerations",
                sql: "\"Purpose\" = 0 OR (\"Version\" > 0 AND \"SourceHash\" IS NOT NULL AND \"SnapshotJson\" IS NOT NULL AND \"PolicyHash\" IS NOT NULL AND \"PolicyJson\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Purpose",
                table: "ProjectModerations",
                sql: "\"Purpose\" IN (0,1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations",
                sql: "(\"Purpose\" = 0 AND \"Status\" BETWEEN 0 AND 5) OR (\"Purpose\" = 1 AND \"Status\" IN (0,1,4,5,6,7))");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceModerationAttempts_ModerationId_ModerationPurpose",
                table: "EvidenceModerationAttempts",
                columns: new[] { "ModerationId", "ModerationPurpose" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EvidenceModerationAttempts_Purpose",
                table: "EvidenceModerationAttempts",
                sql: "\"ModerationPurpose\" = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_EvidenceModerationAttempts_ProjectModerations_ModerationId_~",
                table: "EvidenceModerationAttempts",
                columns: new[] { "ModerationId", "ModerationPurpose" },
                principalTable: "ProjectModerations",
                principalColumns: new[] { "Id", "Purpose" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectModerations_Projects_ProjectId",
                table: "ProjectModerations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_ProjectModerations_EvidenceModerationI~",
                table: "ProjectVerifications",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" },
                principalTable: "ProjectModerations",
                principalColumns: new[] { "Id", "Purpose" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_ProjectModerations_EvidenceModerat~",
                table: "VerificationNotification",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" },
                principalTable: "ProjectModerations",
                principalColumns: new[] { "Id", "Purpose" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.DropTable(
                name: "ProjectEvidenceModerations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE \"ProjectModerations\", \"ProjectVerifications\", \"EvidenceModerationAttempts\", \"VerificationNotification\" IN ACCESS EXCLUSIVE MODE;");

            migrationBuilder.DropForeignKey(
                name: "FK_EvidenceModerationAttempts_ProjectModerations_ModerationId_~",
                table: "EvidenceModerationAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectModerations_Projects_ProjectId",
                table: "ProjectModerations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_ProjectModerations_EvidenceModerationI~",
                table: "ProjectVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_ProjectModerations_EvidenceModerat~",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_EvidenceModerationId_EvidenceModer~",
                table: "VerificationNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationNotification_EvidencePurpose",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId_EvidenceModeratio~",
                table: "ProjectVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_EvidencePurpose",
                table: "ProjectVerifications");

            migrationBuilder.CreateTable(
                name: "ProjectEvidenceModerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PolicyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.Sql("""
INSERT INTO "ProjectEvidenceModerations" ("Id", "ProjectId", "Version", "SourceHash", "SnapshotJson", "PolicyHash", "PolicyJson", "CreatedAt", "Status", "SupersededAt", "CompletedAt", "LeaseExpiresAt", "LeaseToken", "NextRetryAt", "AttemptCount", "LastErrorCode", "LastError", "Summary")
SELECT "Id", "ProjectId", "Version", "SourceHash", "SnapshotJson", "PolicyHash", "PolicyJson", "CreatedAt",
    CASE "Status" WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 6 THEN 2 WHEN 4 THEN 3 WHEN 7 THEN 4 WHEN 5 THEN 5 END,
    "SupersededAt", "CompletedAt", "LeaseExpiresAt", "LeaseToken", "NextRetryAt", "AttemptCount", "LastErrorCode", "LastError", "Summary"
FROM "ProjectModerations" WHERE "Purpose" = 1;
DO $verify$
BEGIN
    IF (SELECT count(*) FROM "ProjectEvidenceModerations") <> (SELECT count(*) FROM "ProjectModerations" WHERE "Purpose" = 1)
    OR EXISTS (
        SELECT 1 FROM "ProjectEvidenceModerations" old
        FULL JOIN (SELECT "Id", "ProjectId", "Version", "SourceHash", "SnapshotJson", "PolicyHash", "PolicyJson", "CreatedAt",
            CASE "Status" WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 6 THEN 2 WHEN 4 THEN 3 WHEN 7 THEN 4 WHEN 5 THEN 5 END::smallint AS "Status",
            "SupersededAt", "CompletedAt", "LeaseExpiresAt", "LeaseToken", "NextRetryAt", "AttemptCount", "LastErrorCode", "LastError", "Summary"
            FROM "ProjectModerations" WHERE "Purpose" = 1) copied ON old."Id" = copied."Id"
        WHERE to_jsonb(old) IS DISTINCT FROM to_jsonb(copied)
    ) THEN RAISE EXCEPTION 'G1 row/key/snapshot/hash/history comparison failed; old table retained by transaction rollback'; END IF;
END $verify$;
DELETE FROM "ProjectModerations" WHERE "Purpose" = 1;
""");
            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectModerations_Id_Purpose",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_ProjectId_Version",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectModerations_Purpose_Status_NextRetryAt",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_AttemptNumber",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_EvidenceSnapshot",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Purpose",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceModerationAttempts_ModerationId_ModerationPurpose",
                table: "EvidenceModerationAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EvidenceModerationAttempts_Purpose",
                table: "EvidenceModerationAttempts");

            migrationBuilder.DropColumn(
                name: "EvidenceModerationPurpose",
                table: "VerificationNotification");

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
                name: "Purpose",
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

            migrationBuilder.DropColumn(
                name: "ModerationPurpose",
                table: "EvidenceModerationAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_EvidenceModerationId",
                table: "VerificationNotification",
                column: "EvidenceModerationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId",
                table: "ProjectVerifications",
                column: "EvidenceModerationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModerations_ProjectId_AttemptNumber",
                table: "ProjectModerations",
                columns: new[] { "ProjectId", "AttemptNumber" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_AttemptNumber",
                table: "ProjectModerations",
                sql: "\"AttemptNumber\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations",
                sql: "\"Status\" BETWEEN 0 AND 5");

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
                name: "FK_EvidenceModerationAttempts_ProjectEvidenceModerations_Moder~",
                table: "EvidenceModerationAttempts",
                column: "ModerationId",
                principalTable: "ProjectEvidenceModerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectModerations_Projects_ProjectId",
                table: "ProjectModerations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_ProjectEvidenceModerations_EvidenceMod~",
                table: "ProjectVerifications",
                column: "EvidenceModerationId",
                principalTable: "ProjectEvidenceModerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_ProjectEvidenceModerations_Evidenc~",
                table: "VerificationNotification",
                column: "EvidenceModerationId",
                principalTable: "ProjectEvidenceModerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
