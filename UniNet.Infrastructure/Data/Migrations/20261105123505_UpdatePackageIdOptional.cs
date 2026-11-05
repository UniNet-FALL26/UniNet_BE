using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePackageIdOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyPackages");

            migrationBuilder.AlterColumn<Guid>(
                name: "PackageId",
                table: "AdvertisementCampaigns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PackageId",
                table: "AdvertisementCampaigns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyPackages_AdvertisementCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "AdvertisementCampaigns",
                        principalColumn: "Id");
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
                name: "IX_CompanyPackages_CampaignId",
                table: "CompanyPackages",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPackages_CompanyUserId",
                table: "CompanyPackages",
                column: "CompanyUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPackages_PackageId",
                table: "CompanyPackages",
                column: "PackageId");
        }
    }
}
