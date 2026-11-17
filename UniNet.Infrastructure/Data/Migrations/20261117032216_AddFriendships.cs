using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LowAccountId = table.Column<Guid>(type: "uuid", nullable: false, computedColumnSql: "CASE WHEN \"RequesterAccountId\" < \"ReceiverAccountId\" THEN \"RequesterAccountId\" ELSE \"ReceiverAccountId\" END", stored: true),
                    HighAccountId = table.Column<Guid>(type: "uuid", nullable: false, computedColumnSql: "CASE WHEN \"RequesterAccountId\" < \"ReceiverAccountId\" THEN \"ReceiverAccountId\" ELSE \"RequesterAccountId\" END", stored: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => x.Id);
                    table.CheckConstraint("CK_Friendships_DifferentAccounts", "\"RequesterAccountId\" <> \"ReceiverAccountId\"");
                    table.CheckConstraint("CK_Friendships_Status", "\"Status\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_Friendships_Accounts_ReceiverAccountId",
                        column: x => x.ReceiverAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Friendships_Accounts_RequesterAccountId",
                        column: x => x.RequesterAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_LowAccountId_HighAccountId",
                table: "Friendships",
                columns: new[] { "LowAccountId", "HighAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_ReceiverAccountId_Status_CreatedAt",
                table: "Friendships",
                columns: new[] { "ReceiverAccountId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_RequesterAccountId_Status_CreatedAt",
                table: "Friendships",
                columns: new[] { "RequesterAccountId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Friendships");
        }
    }
}
