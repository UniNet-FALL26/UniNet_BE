using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SingleVerificationReportAndRefundAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VerificationReports_VerificationAssignments_VerificationId",
                table: "VerificationReports");

            migrationBuilder.RenameColumn(
                name: "VerificationId",
                table: "VerificationReports",
                newName: "AssignmentId");

            migrationBuilder.RenameIndex(
                name: "IX_VerificationReports_VerificationId",
                table: "VerificationReports",
                newName: "IX_VerificationReports_AssignmentId");

            migrationBuilder.AddColumn<string>(
                name: "LegacyOriginalJson",
                table: "VerificationReports",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectVerificationId",
                table: "VerificationReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "RequestSource",
                table: "RefundTransactions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RequestedAt",
                table: "RefundTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "RefundTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""

LOCK TABLE "VerificationReports", "VerificationReportVersions", "VerificationAdminAction", "RefundTransactions" IN SHARE ROW EXCLUSIVE MODE;

-- Keep the complete original report and every old revision/action in their existing tables.
UPDATE "VerificationReports" r SET "LegacyOriginalJson" = to_jsonb(r);

-- Reject inconsistent finalized history instead of inventing a new case verdict.
DO $guard$
BEGIN
    IF EXISTS (
        SELECT 1 FROM (
            SELECT p."Id", p."Status", p."FinalizedAt", v."Outcome" AS revision_outcome, v."Id" AS revision_id,
                row_number() OVER (PARTITION BY p."Id" ORDER BY COALESCE(v."SubmittedAt", r."SubmittetAt") DESC, r."Id") AS rn
            FROM "VerificationReports" r
            JOIN "VerificationAssignments" a ON a."Id" = r."AssignmentId"
            JOIN "ProjectVerifications" p ON p."Id" = a."ProjectVerificationId"
            LEFT JOIN LATERAL (SELECT x.* FROM "VerificationReportVersions" x WHERE x."ReportId" = r."Id"
                ORDER BY x."Version" DESC LIMIT 1) v ON true
        ) history WHERE rn = 1 AND "FinalizedAt" IS NOT NULL AND "Status" IN (1,2)
            AND revision_id IS NOT NULL AND revision_outcome <> "Status"
    ) THEN RAISE EXCEPTION 'Legacy report revision disagrees with finalized case; review history before migration'; END IF;
END $guard$;

WITH latest AS (
    SELECT DISTINCT ON ("ReportId") * FROM "VerificationReportVersions" ORDER BY "ReportId", "Version" DESC
)
UPDATE "VerificationReports" r SET "Summary" = v."Summary", "Findings" = v."Findings",
    "Recommendations" = v."Recommendations", "Outcome" = v."Outcome", "SubmittedByUserId" = v."SubmittedByUserId",
    "SubmittetAt" = v."SubmittedAt", "UpdatedAt" = v."SubmittedAt"
FROM latest v WHERE r."Id" = v."ReportId";

-- Single-report legacy cases can be linked safely. Multiple historical reports remain intact,
-- with null case FKs; new submissions are blocked for any case already containing a report.
WITH single_case AS (
    SELECT a."ProjectVerificationId" AS case_id FROM "VerificationReports" r
    JOIN "VerificationAssignments" a ON a."Id" = r."AssignmentId"
    GROUP BY a."ProjectVerificationId" HAVING count(*) = 1
)
UPDATE "VerificationReports" r SET "ProjectVerificationId" = a."ProjectVerificationId"
FROM "VerificationAssignments" a JOIN single_case c ON c.case_id = a."ProjectVerificationId"
WHERE r."AssignmentId" = a."Id";

-- Only backfill an Admin actor when the old audit unambiguously identifies one person.
WITH actor AS (
    SELECT DISTINCT ON (a."RefundId") a."RefundId", a."ActorUserId", a."CreatedAt"
    FROM "VerificationAdminAction" a
    JOIN "RefundTransactions" refund ON refund."Id" = a."RefundId"
    JOIN "VerificationTransactions" payment ON payment."TransactionId" = refund."TransactionId" AND payment."VerificationId" = a."VerificationId"
    WHERE a."RefundId" IS NOT NULL AND a."Decision" = 'RefundRequested'
    AND NOT EXISTS (SELECT 1 FROM "VerificationAdminAction" other WHERE other."RefundId" = a."RefundId"
        AND other."Decision" = 'RefundRequested' AND other."ActorUserId" <> a."ActorUserId")
    ORDER BY a."RefundId", a."CreatedAt", a."Id"
)
UPDATE "RefundTransactions" r SET "RequestedByUserId" = a."ActorUserId", "RequestedAt" = a."CreatedAt", "RequestSource" = 2
FROM actor a WHERE r."Id" = a."RefundId";

UPDATE "RefundTransactions" r SET "RequestSource" = 1, "RequestedAt" = r."CreatedAt"
WHERE r."Reason" IN ('SYSTEM_TIMEOUT:ASSIGNMENT', 'SYSTEM_TIMEOUT:REPLACEMENT') AND r."RequestSource" = 0
AND NOT EXISTS (SELECT 1 FROM "VerificationAdminAction" a WHERE a."RefundId" = r."Id" AND a."Decision" = 'RefundRequested')
AND EXISTS (SELECT 1 FROM "VerificationTransactions" t JOIN "ProjectVerifications" v ON v."Id" = t."VerificationId"
    WHERE t."TransactionId" = r."TransactionId" AND v."Status" = 3 AND v."CancellationReason" = r."Reason"
        AND v."CancelledAt" IS NOT NULL AND r."CreatedAt" >= v."CancelledAt");
""");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_VerificationAssignments_Id_ProjectVerificationId",
                table: "VerificationAssignments",
                columns: new[] { "Id", "ProjectVerificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReports_AssignmentId_ProjectVerificationId",
                table: "VerificationReports",
                columns: new[] { "AssignmentId", "ProjectVerificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReports_ProjectVerificationId",
                table: "VerificationReports",
                column: "ProjectVerificationId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationReports_Case",
                table: "VerificationReports",
                sql: "\"ProjectVerificationId\" IS NOT NULL OR \"LegacyOriginalJson\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_RequestedByUserId",
                table: "RefundTransactions",
                column: "RequestedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RefundTransactions_Users_RequestedByUserId",
                table: "RefundTransactions",
                column: "RequestedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationReports_ProjectVerifications_ProjectVerificatio~",
                table: "VerificationReports",
                column: "ProjectVerificationId",
                principalTable: "ProjectVerifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationReports_VerificationAssignments_AssignmentId",
                table: "VerificationReports",
                column: "AssignmentId",
                principalTable: "VerificationAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationReports_VerificationAssignments_AssignmentId_Pr~",
                table: "VerificationReports",
                columns: new[] { "AssignmentId", "ProjectVerificationId" },
                principalTable: "VerificationAssignments",
                principalColumns: new[] { "Id", "ProjectVerificationId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefundTransactions_Users_RequestedByUserId",
                table: "RefundTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationReports_ProjectVerifications_ProjectVerificatio~",
                table: "VerificationReports");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationReports_VerificationAssignments_AssignmentId",
                table: "VerificationReports");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationReports_VerificationAssignments_AssignmentId_Pr~",
                table: "VerificationReports");

            migrationBuilder.DropIndex(
                name: "IX_VerificationReports_AssignmentId_ProjectVerificationId",
                table: "VerificationReports");

            migrationBuilder.DropIndex(
                name: "IX_VerificationReports_ProjectVerificationId",
                table: "VerificationReports");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationReports_Case",
                table: "VerificationReports");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_VerificationAssignments_Id_ProjectVerificationId",
                table: "VerificationAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RefundTransactions_RequestedByUserId",
                table: "RefundTransactions");

            migrationBuilder.Sql("""

-- Preserve new manual refund actors in the retained old audit table before removing new columns.
INSERT INTO "VerificationAdminAction" ("Id", "VerificationId", "ActorUserId", "Decision", "Reason", "RefundId", "CreatedAt")
SELECT md5('refund-audit:' || r."Id"::text)::uuid, t."VerificationId", r."RequestedByUserId", 'RefundRequested',
    r."Reason", r."Id", r."RequestedAt"
FROM "RefundTransactions" r JOIN "VerificationTransactions" t ON t."TransactionId" = r."TransactionId"
WHERE r."RequestSource" = 2 AND r."RequestedByUserId" IS NOT NULL AND r."RequestedAt" IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM "VerificationAdminAction" a WHERE a."RefundId" = r."Id" AND a."Decision" = 'RefundRequested');

-- Restore the original base content on rollback. Revision rows have never been removed.
WITH originals AS (
    SELECT r."Id" AS id, jsonb_populate_record(NULL::"VerificationReports", r."LegacyOriginalJson") AS original_row
    FROM "VerificationReports" r WHERE r."LegacyOriginalJson" IS NOT NULL
)
UPDATE "VerificationReports" r SET "Summary" = (o.original_row)."Summary", "Findings" = (o.original_row)."Findings",
    "Recommendations" = (o.original_row)."Recommendations", "Outcome" = (o.original_row)."Outcome",
    "SubmittedByUserId" = (o.original_row)."SubmittedByUserId", "SubmittetAt" = (o.original_row)."SubmittetAt", "UpdatedAt" = (o.original_row)."UpdatedAt"
FROM originals o WHERE r."Id" = o.id;
""");

            migrationBuilder.DropColumn(
                name: "LegacyOriginalJson",
                table: "VerificationReports");

            migrationBuilder.DropColumn(
                name: "ProjectVerificationId",
                table: "VerificationReports");

            migrationBuilder.DropColumn(
                name: "RequestSource",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "RequestedAt",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "RefundTransactions");

            migrationBuilder.RenameColumn(
                name: "AssignmentId",
                table: "VerificationReports",
                newName: "VerificationId");

            migrationBuilder.RenameIndex(
                name: "IX_VerificationReports_AssignmentId",
                table: "VerificationReports",
                newName: "IX_VerificationReports_VerificationId");

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationReports_VerificationAssignments_VerificationId",
                table: "VerificationReports",
                column: "VerificationId",
                principalTable: "VerificationAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
