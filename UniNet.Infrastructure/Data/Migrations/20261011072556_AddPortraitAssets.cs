using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPortraitAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortraitAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAtUnix = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortraitAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortraitAssets_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortraitAssets_AccountId",
                table: "PortraitAssets",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PortraitAssets_ImageUrl",
                table: "PortraitAssets",
                column: "ImageUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortraitAssets_PublicId",
                table: "PortraitAssets",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortraitAssets_State_CreatedAtUnix",
                table: "PortraitAssets",
                columns: new[] { "State", "CreatedAtUnix" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortraitAssets");
        }
    }
}
