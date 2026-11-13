using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppReconciliationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextReconciliationAt",
                table: "GitHubAppInstallations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationFailures",
                table: "GitHubAppInstallations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "VerificationUnknown",
                table: "GitHubAppInstallations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallations_NextReconciliationAt",
                table: "GitHubAppInstallations",
                column: "NextReconciliationAt");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitHubAppInstallation_ReconciliationFailures",
                table: "GitHubAppInstallations",
                sql: "\"ReconciliationFailures\" >= 0 AND \"ReconciliationFailures\" <= 16");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GitHubAppInstallations_NextReconciliationAt",
                table: "GitHubAppInstallations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitHubAppInstallation_ReconciliationFailures",
                table: "GitHubAppInstallations");

            migrationBuilder.DropColumn(
                name: "NextReconciliationAt",
                table: "GitHubAppInstallations");

            migrationBuilder.DropColumn(
                name: "ReconciliationFailures",
                table: "GitHubAppInstallations");

            migrationBuilder.DropColumn(
                name: "VerificationUnknown",
                table: "GitHubAppInstallations");
        }
    }
}
