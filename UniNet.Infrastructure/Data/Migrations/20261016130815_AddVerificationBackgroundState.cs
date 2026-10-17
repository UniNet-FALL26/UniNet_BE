using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationBackgroundState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchStartedAt",
                table: "RefundTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextReconciliationAt",
                table: "RefundTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationAttempts",
                table: "RefundTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiAttemptCount",
                table: "ProjectVerifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiAttemptEvidenceVersion",
                table: "ProjectVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiLastError",
                table: "ProjectVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiNextRetryAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerificationNotification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationNotification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationNotification_ProjectVerifications_VerificationId",
                        column: x => x.VerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationNotification_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_EventKey_RecipientUserId",
                table: "VerificationNotification",
                columns: new[] { "EventKey", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_RecipientUserId_CreatedAt",
                table: "VerificationNotification",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_VerificationId",
                table: "VerificationNotification",
                column: "VerificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerificationNotification");

            migrationBuilder.DropColumn(
                name: "DispatchStartedAt",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "NextReconciliationAt",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ReconciliationAttempts",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "AiAttemptCount",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiAttemptEvidenceVersion",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiLastError",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiNextRetryAt",
                table: "ProjectVerifications");
        }
    }
}
