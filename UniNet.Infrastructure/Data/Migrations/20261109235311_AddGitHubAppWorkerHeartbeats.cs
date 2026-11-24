using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppWorkerHeartbeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubAppWorkerHeartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WorkerAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumerAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReconciliationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppWorkerHeartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppWorkerHeartbeats_UpdatedAt",
                table: "GitHubAppWorkerHeartbeats",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubAppWorkerHeartbeats");
        }
    }
}
