using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppWebhookRequeueAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubAppWebhookRequeueAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PreviousErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppWebhookRequeueAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubAppWebhookRequeueAudits_Accounts_ActorAccountId",
                        column: x => x.ActorAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitHubAppWebhookRequeueAudits_GitHubAppWebhookDeliveries_De~",
                        column: x => x.DeliveryId,
                        principalTable: "GitHubAppWebhookDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppWebhookRequeueAudits_ActorAccountId",
                table: "GitHubAppWebhookRequeueAudits",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppWebhookRequeueAudits_DeliveryId_CreatedAt",
                table: "GitHubAppWebhookRequeueAudits",
                columns: new[] { "DeliveryId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve operational audit: a populated table must be archived by an
            // operator before a downgrade can be considered.
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "GitHubAppWebhookRequeueAudits") THEN
                        RAISE EXCEPTION 'Cannot downgrade while GitHub App requeue audit records exist';
                    END IF;
                END $migration$;
                """);
            migrationBuilder.DropTable(
                name: "GitHubAppWebhookRequeueAudits");
        }
    }
}
