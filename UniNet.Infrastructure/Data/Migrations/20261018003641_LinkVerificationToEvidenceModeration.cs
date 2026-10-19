using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkVerificationToEvidenceModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceModerationId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId",
                table: "ProjectVerifications",
                column: "EvidenceModerationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_ProjectEvidenceModerations_EvidenceMod~",
                table: "ProjectVerifications",
                column: "EvidenceModerationId",
                principalTable: "ProjectEvidenceModerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_ProjectEvidenceModerations_EvidenceMod~",
                table: "ProjectVerifications");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_EvidenceModerationId",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "EvidenceModerationId",
                table: "ProjectVerifications");
        }
    }
}
