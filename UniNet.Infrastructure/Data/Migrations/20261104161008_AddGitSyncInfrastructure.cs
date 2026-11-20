using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitSyncInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SyncIntervalHours",
                table: "ProjectGitRepositories",
                type: "integer",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.CreateTable(
                name: "GitSyncJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SyncType = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitSyncJobs", x => x.Id);
                    table.CheckConstraint("CK_GitSyncJobs_CompletedAt", "(\"Status\" IN (2, 3, 5) AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" NOT IN (2, 3, 5) AND \"CompletedAt\" IS NULL)");
                    table.CheckConstraint("CK_GitSyncJobs_Lease", "(\"LeaseId\" IS NULL) = (\"LeaseExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_GitSyncJobs_RetryCount", "\"RetryCount\" >= 0");
                    table.CheckConstraint("CK_GitSyncJobs_Status", "\"Status\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_GitSyncJobs_Type", "\"SyncType\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_GitSyncJobs_GitHubOAuthConnections_OAuthConnectionId",
                        column: x => x.OAuthConnectionId,
                        principalTable: "GitHubOAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitSyncJobs_GitHubRepositories_GitHubRepositoryId",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "GitHubRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitSyncJobs_ProjectGitRepositories_ProjectGitRepositoryId",
                        column: x => x.ProjectGitRepositoryId,
                        principalTable: "ProjectGitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitSyncJobs_Users_RequestedByProfileId",
                        column: x => x.RequestedByProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitSyncCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Cursor = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ItemsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    LastProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitSyncCheckpoints", x => x.Id);
                    table.CheckConstraint("CK_GitSyncCheckpoints_ItemsProcessed", "\"ItemsProcessed\" >= 0");
                    table.CheckConstraint("CK_GitSyncCheckpoints_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_GitSyncCheckpoints_GitSyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "GitSyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectGitRepositories_SyncIntervalHours",
                table: "ProjectGitRepositories",
                sql: "\"SyncIntervalHours\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncCheckpoints_SyncJobId_Category_ScopeKey",
                table: "GitSyncCheckpoints",
                columns: new[] { "SyncJobId", "Category", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_GitHubRepositoryId",
                table: "GitSyncJobs",
                column: "GitHubRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_OAuthConnectionId",
                table: "GitSyncJobs",
                column: "OAuthConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_ProjectGitRepositoryId_CreatedAt",
                table: "GitSyncJobs",
                columns: new[] { "ProjectGitRepositoryId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_ProjectGitRepositoryId_IdempotencyKey",
                table: "GitSyncJobs",
                columns: new[] { "ProjectGitRepositoryId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_ProjectGitRepositoryId_Status",
                table: "GitSyncJobs",
                columns: new[] { "ProjectGitRepositoryId", "Status" },
                unique: true,
                filter: "\"Status\" IN (0, 1, 4, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_RequestedByProfileId",
                table: "GitSyncJobs",
                column: "RequestedByProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_Status_NextAttemptAt_LeaseExpiresAt_CreatedAt",
                table: "GitSyncJobs",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitSyncCheckpoints");

            migrationBuilder.DropTable(
                name: "GitSyncJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectGitRepositories_SyncIntervalHours",
                table: "ProjectGitRepositories");

            migrationBuilder.DropColumn(
                name: "SyncIntervalHours",
                table: "ProjectGitRepositories");
        }
    }
}
