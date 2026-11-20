using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppWebhookDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubAppWebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderInstallationId = table.Column<long>(type: "bigint", nullable: true),
                    ProviderRepositoryId = table.Column<long>(type: "bigint", nullable: true),
                    Route = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadEncrypted = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppWebhookDeliveries", x => x.Id);
                    table.CheckConstraint("CK_GitHubAppWebhookDelivery_Attempts", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_GitHubAppWebhookDelivery_Route", "\"Route\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_GitHubAppWebhookDelivery_Status", "\"Status\" IN (0, 1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppWebhookDeliveries_DeliveryId",
                table: "GitHubAppWebhookDeliveries",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppWebhookDeliveries_ProviderInstallationId_ProviderR~",
                table: "GitHubAppWebhookDeliveries",
                columns: new[] { "ProviderInstallationId", "ProviderRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppWebhookDeliveries_Status_NextAttemptAt",
                table: "GitHubAppWebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubAppWebhookDeliveries");
        }
    }
}
