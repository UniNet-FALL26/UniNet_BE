using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixAdFlowAndRejectionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "CompanyPackages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "BudgetCap",
                table: "AdvertisementCampaigns",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "AdvertisementCampaigns",
                type: "text",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AdvertisementTransactions",
                table: "AdvertisementTransactions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPackages_CampaignId",
                table: "CompanyPackages",
                column: "CampaignId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyPackages_AdvertisementCampaigns_CampaignId",
                table: "CompanyPackages",
                column: "CampaignId",
                principalTable: "AdvertisementCampaigns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropForeignKey(
                name: "FK_CompanyPackages_AdvertisementCampaigns_CampaignId",
                table: "CompanyPackages");

            migrationBuilder.DropIndex(
                name: "IX_CompanyPackages_CampaignId",
                table: "CompanyPackages");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisementCampaigns_PackageId",
                table: "AdvertisementCampaigns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AdvertisementTransactions",
                table: "AdvertisementTransactions");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "CompanyPackages");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "AdvertisementCampaigns");

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

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementCampaigns_CompanyPackageId",
                table: "AdvertisementCampaigns",
                column: "CompanyPackageId",
                unique: true);

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
    }
}
