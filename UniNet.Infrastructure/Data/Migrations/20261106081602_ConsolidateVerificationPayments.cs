using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateVerificationPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VerificationId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerificationReservationId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "VerificationTransactions" v
                        JOIN "Transactions" t ON t."Id" = v."TransactionId"
                        WHERE t."Status" = 0
                          AND (t."ExpiredAt" IS NULL OR t."ExpiredAt" > CURRENT_TIMESTAMP)
                          AND NULLIF(btrim(t."PaymentUrl"), '') IS NOT NULL
                          AND NULLIF(btrim(t."PaymentQrCode"), '') IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Resolve pending verification checkouts with a URL and no QR before migrating.';
                    END IF;
                END $$;

                UPDATE "Transactions" t
                SET "VerificationId" = v."VerificationId",
                    "VerificationReservationId" = v."ReservationId"
                FROM "VerificationTransactions" v
                WHERE t."Id" = v."TransactionId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_VerificationId",
                table: "Transactions",
                column: "VerificationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_ProjectVerifications_VerificationId",
                table: "Transactions",
                column: "VerificationId",
                principalTable: "ProjectVerifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(name: "VerificationTransactions");
            migrationBuilder.DropColumn(name: "PaymentUrl", table: "Transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_ProjectVerifications_VerificationId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_VerificationId",
                table: "Transactions");

            migrationBuilder.AddColumn<string>(
                name: "PaymentUrl",
                table: "Transactions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerificationTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationTransactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_VerificationTransactions_ProjectVerifications_VerificationId",
                        column: x => x.VerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationTransactions_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationTransactions_VerificationId",
                table: "VerificationTransactions",
                column: "VerificationId");

            migrationBuilder.Sql("""
                INSERT INTO "VerificationTransactions"
                    ("TransactionId", "VerificationId", "ReservationId", "CreatedAt", "UpdatedAt")
                SELECT "Id", "VerificationId", "VerificationReservationId", "CreatedAt", "UpdatedAt"
                FROM "Transactions"
                WHERE "VerificationId" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(name: "VerificationId", table: "Transactions");
            migrationBuilder.DropColumn(name: "VerificationReservationId", table: "Transactions");
        }
    }
}
