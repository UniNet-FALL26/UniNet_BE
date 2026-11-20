using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppWebhookConsumer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GitHubAppWebhookDelivery_Status",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "GitHubAppWebhookDeliveries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "GitHubAppWebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "GitHubAppWebhookDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAt",
                table: "GitHubAppWebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitHubAppWebhookDelivery_Lease",
                table: "GitHubAppWebhookDeliveries",
                sql: "(\"Status\" = 3 AND \"LeaseId\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL) OR (\"Status\" <> 3 AND \"LeaseId\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitHubAppWebhookDelivery_Status",
                table: "GitHubAppWebhookDeliveries",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GitHubAppWebhookDelivery_Lease",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitHubAppWebhookDelivery_Status",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "GitHubAppWebhookDeliveries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitHubAppWebhookDelivery_Status",
                table: "GitHubAppWebhookDeliveries",
                sql: "\"Status\" IN (0, 1, 2)");
        }
    }
}
