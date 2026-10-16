using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationAdminActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VerificationAdminAction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationAdminAction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationAdminAction_ProjectVerifications_VerificationId",
                        column: x => x.VerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationAdminAction_RefundTransactions_RefundId",
                        column: x => x.RefundId,
                        principalTable: "RefundTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationAdminAction_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAdminAction_ActorUserId",
                table: "VerificationAdminAction",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAdminAction_RefundId",
                table: "VerificationAdminAction",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAdminAction_VerificationId_CreatedAt",
                table: "VerificationAdminAction",
                columns: new[] { "VerificationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerificationAdminAction");
        }
    }
}
