using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayOSPaymentReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "FinalizedByUserId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "AiAdditionalEvidenceJson",
                table: "ProjectVerifications",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "AiEvidenceVersion",
                table: "ProjectVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiReviewedAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "ProjectVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true);

            // Earlier verification code added PackageId/evidence to the model without a migration.
            // Preserve the historical package if all existing attempts agree. For unpaid requests,
            // backfill only when there is exactly one active Student verification package.
            // Ambiguous historical requests must be mapped explicitly, never assigned an arbitrary fee.
            migrationBuilder.Sql("""
                UPDATE "ProjectVerifications" v
                SET "PackageId" = (
                    SELECT t."PackageId" FROM "VerificationTransactions" vt
                    JOIN "Transactions" t ON t."Id" = vt."TransactionId"
                    WHERE vt."VerificationId" = v."Id"
                    GROUP BY t."PackageId"
                    HAVING (SELECT COUNT(DISTINCT t2."PackageId") FROM "VerificationTransactions" vt2
                        JOIN "Transactions" t2 ON t2."Id" = vt2."TransactionId"
                        WHERE vt2."VerificationId" = v."Id") = 1
                );
                UPDATE "ProjectVerifications" v
                SET "PackageId" = (SELECT "Id" FROM "Packages"
                    WHERE "Type" = 0 AND "Role" = 0 AND "IsActive" = TRUE)
                WHERE v."PackageId" IS NULL
                    AND NOT EXISTS (SELECT 1 FROM "VerificationTransactions" vt WHERE vt."VerificationId" = v."Id")
                    AND (SELECT COUNT(*) FROM "Packages" WHERE "Type" = 0 AND "Role" = 0 AND "IsActive" = TRUE) = 1;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ProjectVerifications" WHERE "PackageId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot infer package for historical verification. Explicitly map historical requests before applying the payment migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<Guid>(
                name: "PackageId", table: "ProjectVerifications", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectVerificationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_Transactions_PaymentMethod_ProviderOrderCode",
                table: "Transactions",
                columns: new[] { "PaymentMethod", "ProviderOrderCode" },
                unique: true,
                filter: "\"PaymentMethod\" = 'PayOS' AND \"ProviderOrderCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PaymentMethod_ProviderPaymentLinkId",
                table: "Transactions",
                columns: new[] { "PaymentMethod", "ProviderPaymentLinkId" },
                unique: true,
                filter: "\"PaymentMethod\" = 'PayOS' AND \"ProviderPaymentLinkId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_PackageId",
                table: "ProjectVerifications",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_PendingProject",
                table: "ProjectVerifications",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerificationEvidence_ProjectVerificationId_Version",
                table: "ProjectVerificationEvidence",
                columns: new[] { "ProjectVerificationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerificationEvidence_SubmittedByUserId",
                table: "ProjectVerificationEvidence",
                column: "SubmittedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_Packages_PackageId",
                table: "ProjectVerifications",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_Packages_PackageId",
                table: "ProjectVerifications");

            migrationBuilder.DropTable(
                name: "ProjectVerificationEvidence");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_PaymentMethod_ProviderOrderCode",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_PaymentMethod_ProviderPaymentLinkId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_PackageId",
                table: "ProjectVerifications");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_PendingProject",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiAdditionalEvidenceJson",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiEvidenceVersion",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiReviewedAt",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "ProjectVerifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "FinalizedByUserId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
