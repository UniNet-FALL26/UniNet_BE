using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAdvertisementFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisementCampaigns_Packages_PackageId",
                table: "AdvertisementCampaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisementTransactions_AdvertisementCampaigns_CampaignId",
                table: "AdvertisementTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisementTransactions_Transactions_TransactionId",
                table: "AdvertisementTransactions");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisementCampaigns_PackageId",
                table: "AdvertisementCampaigns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AdvertisementTransactions",
                table: "AdvertisementTransactions");

            migrationBuilder.RenameTable(
                name: "AdvertisementTransactions",
                newName: "AdvertisementTransaction");

            migrationBuilder.RenameColumn(
                name: "PackageId",
                table: "AdvertisementCampaigns",
                newName: "CompanyPackageId");

            migrationBuilder.RenameIndex(
                name: "IX_AdvertisementTransactions_TransactionId",
                table: "AdvertisementTransaction",
                newName: "IX_AdvertisementTransaction_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_AdvertisementTransactions_CampaignId",
                table: "AdvertisementTransaction",
                newName: "IX_AdvertisementTransaction_CampaignId");

            migrationBuilder.AlterColumn<decimal>(
                name: "BudgetCap",
                table: "AdvertisementCampaigns",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AdvertisementTransaction",
                table: "AdvertisementTransaction",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "CompanyPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyPackages_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyPackages_Users_CompanyUserId",
                        column: x => x.CompanyUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementCampaigns_CompanyPackageId",
                table: "AdvertisementCampaigns",
                column: "CompanyPackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPackages_CompanyUserId",
                table: "CompanyPackages",
                column: "CompanyUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPackages_PackageId",
                table: "CompanyPackages",
                column: "PackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisementCampaigns_CompanyPackages_CompanyPackageId",
                table: "AdvertisementCampaigns",
                column: "CompanyPackageId",
                principalTable: "CompanyPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisementTransaction_AdvertisementCampaigns_CampaignId",
                table: "AdvertisementTransaction",
                column: "CampaignId",
                principalTable: "AdvertisementCampaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisementTransaction_Transactions_TransactionId",
                table: "AdvertisementTransaction",
                column: "TransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisementCampaigns_CompanyPackages_CompanyPackageId",
                table: "AdvertisementCampaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisementTransaction_AdvertisementCampaigns_CampaignId",
                table: "AdvertisementTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisementTransaction_Transactions_TransactionId",
                table: "AdvertisementTransaction");

            migrationBuilder.DropTable(
                name: "CompanyPackages");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisementCampaigns_CompanyPackageId",
                table: "AdvertisementCampaigns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AdvertisementTransaction",
                table: "AdvertisementTransaction");

            migrationBuilder.RenameTable(
                name: "AdvertisementTransaction",
                newName: "AdvertisementTransactions");

            migrationBuilder.RenameColumn(
                name: "CompanyPackageId",
                table: "AdvertisementCampaigns",
                newName: "PackageId");

            migrationBuilder.RenameIndex(
                name: "IX_AdvertisementTransaction_TransactionId",
                table: "AdvertisementTransactions",
                newName: "IX_AdvertisementTransactions_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_AdvertisementTransaction_CampaignId",
                table: "AdvertisementTransactions",
                newName: "IX_AdvertisementTransactions_CampaignId");

            migrationBuilder.AlterColumn<decimal>(
                name: "BudgetCap",
                table: "AdvertisementCampaigns",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AdvertisementTransactions",
                table: "AdvertisementTransactions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementCampaigns_PackageId",
                table: "AdvertisementCampaigns",
                column: "PackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisementCampaigns_Packages_PackageId",
                table: "AdvertisementCampaigns",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisementTransactions_AdvertisementCampaigns_CampaignId",
                table: "AdvertisementTransactions",
                column: "CampaignId",
                principalTable: "AdvertisementCampaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisementTransactions_Transactions_TransactionId",
                table: "AdvertisementTransactions",
                column: "TransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
