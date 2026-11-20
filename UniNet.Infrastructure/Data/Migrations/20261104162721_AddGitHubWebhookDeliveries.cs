using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubWebhookDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubWebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderRepositoryId = table.Column<long>(type: "bigint", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnqueuedJobCount = table.Column<int>(type: "integer", nullable: false),
                    IsSupportedEvent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubWebhookDeliveries", x => x.Id);
                    table.CheckConstraint("CK_GitHubWebhookDeliveries_EnqueuedJobCount", "\"EnqueuedJobCount\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubWebhookDeliveries_DeliveryId",
                table: "GitHubWebhookDeliveries",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubWebhookDeliveries_ProviderRepositoryId_ReceivedAt",
                table: "GitHubWebhookDeliveries",
                columns: new[] { "ProviderRepositoryId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubWebhookDeliveries");
        }
    }
}
