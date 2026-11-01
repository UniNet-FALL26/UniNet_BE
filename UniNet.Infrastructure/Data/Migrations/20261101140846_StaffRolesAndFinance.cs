using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffRolesAndFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_SingleAdmin",
                table: "Accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts");

            // Numeric roles changed: remap once, atomically, before restoring constraints/index.
            // Revoke old sessions so cached numeric role claims cannot impersonate a new role.
            migrationBuilder.Sql("""
                UPDATE "Accounts" SET "SessionVersion" = "SessionVersion" + 1
                WHERE "Role" IN (2, 3, 4);
                UPDATE "RefreshTokens" SET "RevokedAt" = CURRENT_TIMESTAMP
                WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "Role" IN (2, 3, 4)) AND "RevokedAt" IS NULL;
                UPDATE "NotificationBroadcasts" SET "Role" = CASE "Role" WHEN 'Verificator' THEN 'Assessor' WHEN '2' THEN 'Admin' WHEN '3' THEN 'Moderator' WHEN '4' THEN 'Assessor' ELSE "Role" END;
                UPDATE "Accounts" SET "Role" = CASE "Role" WHEN 2 THEN 6 WHEN 3 THEN 4 WHEN 4 THEN 2 ELSE "Role" END;
                """);

            migrationBuilder.AddColumn<short>(
                name: "Approval",
                table: "RefundTransactions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByAccountId",
                table: "RefundTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualBankReference",
                table: "RefundTransactions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualPayingAccount",
                table: "RefundTransactions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestKey",
                table: "RefundTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "RefundTransactions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "RefundTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByAccountId",
                table: "RefundTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WalletWithdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false),
                    BankName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BankAccountNumber = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BankAccountHolder = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OrderedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayingAccount = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    BankReference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletWithdrawals", x => x.Id);
                    table.CheckConstraint("CK_WalletWithdrawals_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_WalletWithdrawals_State", "\"State\" BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_WalletWithdrawals_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletWithdrawals_Accounts_ConfirmedBy",
                        column: x => x.ConfirmedBy,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletWithdrawals_Accounts_OrderedBy",
                        column: x => x.OrderedBy,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletWithdrawals_Accounts_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WithdrawalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletEntries", x => x.Id);
                    table.CheckConstraint("CK_WalletEntries_Amount", "\"Amount\" <> 0 AND ((\"Amount\" > 0 AND \"WithdrawalId\" IS NULL) OR (\"Amount\" < 0 AND \"WithdrawalId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_WalletEntries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletEntries_Accounts_ActorAccountId",
                        column: x => x.ActorAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletEntries_WalletWithdrawals_WithdrawalId",
                        column: x => x.WithdrawalId,
                        principalTable: "WalletWithdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_ConfirmedByAccountId",
                table: "RefundTransactions",
                column: "ConfirmedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_ManualPayingAccount_ManualBankReference",
                table: "RefundTransactions",
                columns: new[] { "ManualPayingAccount", "ManualBankReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_RequestKey",
                table: "RefundTransactions",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_ReviewedByAccountId",
                table: "RefundTransactions",
                column: "ReviewedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_SingleAdmin",
                table: "Accounts",
                column: "Role",
                unique: true,
                filter: "\"Role\" = 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts",
                sql: "\"Role\" BETWEEN 0 AND 6");

            migrationBuilder.CreateIndex(
                name: "IX_WalletEntries_AccountId_SourceReference",
                table: "WalletEntries",
                columns: new[] { "AccountId", "SourceReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletEntries_ActorAccountId",
                table: "WalletEntries",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletEntries_WithdrawalId",
                table: "WalletEntries",
                column: "WithdrawalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_AccountId_RequestKey",
                table: "WalletWithdrawals",
                columns: new[] { "AccountId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_ConfirmedBy",
                table: "WalletWithdrawals",
                column: "ConfirmedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_OrderedBy",
                table: "WalletWithdrawals",
                column: "OrderedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_PayingAccount_BankReference",
                table: "WalletWithdrawals",
                columns: new[] { "PayingAccount", "BankReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_ReviewedBy",
                table: "WalletWithdrawals",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_State_CreatedAt",
                table: "WalletWithdrawals",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_RefundTransactions_Accounts_ConfirmedByAccountId",
                table: "RefundTransactions",
                column: "ConfirmedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefundTransactions_Accounts_ReviewedByAccountId",
                table: "RefundTransactions",
                column: "ReviewedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "WalletEntries") OR EXISTS (SELECT 1 FROM "WalletWithdrawals")
                     OR EXISTS (SELECT 1 FROM "RefundTransactions" WHERE "Approval" <> 0)
                     OR EXISTS (SELECT 1 FROM "NotificationBroadcasts" WHERE "Role" IN ('Verifier', 'Accountant')) THEN
                    RAISE EXCEPTION 'Financial history requires an explicit archival migration before downgrade';
                  END IF;
                  IF EXISTS (SELECT 1 FROM "Accounts" WHERE "Role" IN (3, 5)) THEN
                    RAISE EXCEPTION 'Cannot downgrade while Verifier/Accountant accounts exist';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_RefundTransactions_Accounts_ConfirmedByAccountId",
                table: "RefundTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_RefundTransactions_Accounts_ReviewedByAccountId",
                table: "RefundTransactions");

            migrationBuilder.DropTable(
                name: "WalletEntries");

            migrationBuilder.DropTable(
                name: "WalletWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_RefundTransactions_ConfirmedByAccountId",
                table: "RefundTransactions");

            migrationBuilder.DropIndex(
                name: "IX_RefundTransactions_ManualPayingAccount_ManualBankReference",
                table: "RefundTransactions");

            migrationBuilder.DropIndex(
                name: "IX_RefundTransactions_RequestKey",
                table: "RefundTransactions");

            migrationBuilder.DropIndex(
                name: "IX_RefundTransactions_ReviewedByAccountId",
                table: "RefundTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_SingleAdmin",
                table: "Accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Approval",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ConfirmedByAccountId",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ManualBankReference",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ManualPayingAccount",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "RefundTransactions");

            migrationBuilder.DropColumn(
                name: "ReviewedByAccountId",
                table: "RefundTransactions");

            migrationBuilder.Sql("""
                UPDATE "Accounts" SET "SessionVersion" = "SessionVersion" + 1 WHERE "Role" IN (2, 4, 6);
                UPDATE "RefreshTokens" SET "RevokedAt" = CURRENT_TIMESTAMP WHERE "RevokedAt" IS NULL;
                UPDATE "NotificationBroadcasts" SET "Role" = 'Verificator' WHERE "Role" = 'Assessor';
                UPDATE "Accounts" SET "Role" = CASE "Role" WHEN 6 THEN 2 WHEN 4 THEN 3 WHEN 2 THEN 4 ELSE "Role" END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_SingleAdmin",
                table: "Accounts",
                column: "Role",
                unique: true,
                filter: "\"Role\" = 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Role",
                table: "Accounts",
                sql: "\"Role\" BETWEEN 0 AND 4");
        }
    }
}
