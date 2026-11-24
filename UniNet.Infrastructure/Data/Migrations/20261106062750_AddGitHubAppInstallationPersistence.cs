using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppInstallationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubAppAuthorizationFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReturnTarget = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppAuthorizationFlows", x => x.Id);
                    table.CheckConstraint("CK_GitHubAppAuthorizationFlow_Status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_GitHubAppAuthorizationFlows_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitHubAppAuthorizationFlows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitHubAppInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderInstallationId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerLogin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppInstallations", x => x.Id);
                    table.CheckConstraint("CK_GitHubAppInstallation_ProviderIds", "\"ProviderInstallationId\" > 0 AND \"OwnerAccountId\" > 0");
                    table.CheckConstraint("CK_GitHubAppInstallation_Status", "\"Status\" IN (0, 1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "GitHubAppInstallationAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppInstallationAccounts", x => x.Id);
                    table.CheckConstraint("CK_GitHubAppInstallationAccount_User", "\"GitHubUserId\" > 0");
                    table.ForeignKey(
                        name: "FK_GitHubAppInstallationAccounts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitHubAppInstallationAccounts_GitHubAppInstallations_Instal~",
                        column: x => x.InstallationId,
                        principalTable: "GitHubAppInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitHubAppRepositoryGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderRepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppRepositoryGrants", x => x.Id);
                    table.CheckConstraint("CK_GitHubAppRepositoryGrant_Repository", "\"ProviderRepositoryId\" > 0");
                    table.ForeignKey(
                        name: "FK_GitHubAppRepositoryGrants_GitHubAppInstallations_Installati~",
                        column: x => x.InstallationId,
                        principalTable: "GitHubAppInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppAuthorizationFlows_AccountId_Status_ExpiresAt",
                table: "GitHubAppAuthorizationFlows",
                columns: new[] { "AccountId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppAuthorizationFlows_ProjectId",
                table: "GitHubAppAuthorizationFlows",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppAuthorizationFlows_StateHash",
                table: "GitHubAppAuthorizationFlows",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallationAccounts_AccountId",
                table: "GitHubAppInstallationAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallationAccounts_InstallationId_AccountId",
                table: "GitHubAppInstallationAccounts",
                columns: new[] { "InstallationId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppInstallations_ProviderInstallationId",
                table: "GitHubAppInstallations",
                column: "ProviderInstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppRepositoryGrants_InstallationId_ProviderRepository~",
                table: "GitHubAppRepositoryGrants",
                columns: new[] { "InstallationId", "ProviderRepositoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubAppAuthorizationFlows");

            migrationBuilder.DropTable(
                name: "GitHubAppInstallationAccounts");

            migrationBuilder.DropTable(
                name: "GitHubAppRepositoryGrants");

            migrationBuilder.DropTable(
                name: "GitHubAppInstallations");
        }
    }
}
