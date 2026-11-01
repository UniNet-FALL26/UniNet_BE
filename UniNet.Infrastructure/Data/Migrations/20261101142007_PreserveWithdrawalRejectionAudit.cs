using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveWithdrawalRejectionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "WalletWithdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedBy",
                table: "WalletWithdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "WalletWithdrawals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_RejectedBy",
                table: "WalletWithdrawals",
                column: "RejectedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_WalletWithdrawals_Accounts_RejectedBy",
                table: "WalletWithdrawals",
                column: "RejectedBy",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WalletWithdrawals_Accounts_RejectedBy",
                table: "WalletWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_WalletWithdrawals_RejectedBy",
                table: "WalletWithdrawals");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "WalletWithdrawals");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                table: "WalletWithdrawals");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "WalletWithdrawals");
        }
    }
}
