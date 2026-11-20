using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubRepositoryIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubOAuthAuthorizationFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReturnTarget = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PendingProviderAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PendingLogin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PendingDisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PendingAvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PendingAccessTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    PendingRefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    PendingGrantedScopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PendingAccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingRefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubOAuthAuthorizationFlows", x => x.Id);
                    table.CheckConstraint("CK_GitHubOAuthAuthorizationFlows_PendingTokens", "(\"Status\" <> 2) OR (\"PendingAccessTokenEncrypted\" IS NOT NULL AND \"PendingProviderAccountId\" IS NOT NULL)");
                    table.CheckConstraint("CK_GitHubOAuthAuthorizationFlows_Status", "\"Status\" BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_GitHubOAuthAuthorizationFlows_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubOAuthConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Login = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    GrantedScopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubOAuthConnections", x => x.Id);
                    table.CheckConstraint("CK_GitHubOAuthConnections_RevokedAt", "(\"Status\" = 2 AND \"RevokedAt\" IS NOT NULL) OR (\"Status\" <> 2 AND \"RevokedAt\" IS NULL)");
                    table.CheckConstraint("CK_GitHubOAuthConnections_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_GitHubOAuthConnections_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitHubRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderRepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    ProviderOwnerId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerLogin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "character varying(511)", maxLength: 511, nullable: false),
                    HtmlUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultBranch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsPrivate = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubRepositories", x => x.Id);
                    table.CheckConstraint("CK_GitHubRepositories_ProviderIds", "\"ProviderRepositoryId\" > 0 AND \"ProviderOwnerId\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "ProjectGitManagerGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedByProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByProfileId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitManagerGrants", x => x.Id);
                    table.CheckConstraint("CK_ProjectGitManagerGrants_ActiveRevocation", "(\"IsActive\" AND \"RevokedAt\" IS NULL AND \"RevokedByProfileId\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_ProjectGitManagerGrants_DifferentProfiles", "\"UserProfileId\" <> \"GrantedByProfileId\"");
                    table.ForeignKey(
                        name: "FK_ProjectGitManagerGrants_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitManagerGrants_Users_GrantedByProfileId",
                        column: x => x.GrantedByProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitManagerGrants_Users_RevokedByProfileId",
                        column: x => x.RevokedByProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitManagerGrants_Users_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGitRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OAuthConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedByProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessStatus = table.Column<short>(type: "smallint", nullable: false),
                    SyncPaused = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnlinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAccessCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitRepositories", x => x.Id);
                    table.CheckConstraint("CK_ProjectGitRepositories_AccessStatus", "\"AccessStatus\" BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_ProjectGitRepositories_UnlinkedAt", "(\"UnlinkedAt\" IS NULL) OR (\"UnlinkedAt\" >= \"LinkedAt\")");
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositories_GitHubOAuthConnections_OAuthConnecti~",
                        column: x => x.OAuthConnectionId,
                        principalTable: "GitHubOAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositories_GitHubRepositories_GitHubRepositoryId",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "GitHubRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositories_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositories_Users_LinkedByProfileId",
                        column: x => x.LinkedByProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGitRepositoryHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    PreviousOAuthConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewOAuthConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitRepositoryHistory", x => x.Id);
                    table.CheckConstraint("CK_ProjectGitRepositoryHistory_Action", "\"Action\" BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositoryHistory_GitHubOAuthConnections_NewOAuth~",
                        column: x => x.NewOAuthConnectionId,
                        principalTable: "GitHubOAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositoryHistory_GitHubOAuthConnections_Previous~",
                        column: x => x.PreviousOAuthConnectionId,
                        principalTable: "GitHubOAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositoryHistory_ProjectGitRepositories_ProjectG~",
                        column: x => x.ProjectGitRepositoryId,
                        principalTable: "ProjectGitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitRepositoryHistory_Users_ActorProfileId",
                        column: x => x.ActorProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubOAuthAuthorizationFlows_AccountId_Status_ExpiresAt",
                table: "GitHubOAuthAuthorizationFlows",
                columns: new[] { "AccountId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubOAuthAuthorizationFlows_StateHash",
                table: "GitHubOAuthAuthorizationFlows",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubOAuthConnections_AccountId",
                table: "GitHubOAuthConnections",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubOAuthConnections_AccountId_ProviderAccountId",
                table: "GitHubOAuthConnections",
                columns: new[] { "AccountId", "ProviderAccountId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubRepositories_FullName",
                table: "GitHubRepositories",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubRepositories_ProviderRepositoryId",
                table: "GitHubRepositories",
                column: "ProviderRepositoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitManagerGrants_GrantedByProfileId",
                table: "ProjectGitManagerGrants",
                column: "GrantedByProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitManagerGrants_ProjectId_UserProfileId",
                table: "ProjectGitManagerGrants",
                columns: new[] { "ProjectId", "UserProfileId" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitManagerGrants_ProjectId_UserProfileId_IsActive",
                table: "ProjectGitManagerGrants",
                columns: new[] { "ProjectId", "UserProfileId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitManagerGrants_RevokedByProfileId",
                table: "ProjectGitManagerGrants",
                column: "RevokedByProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitManagerGrants_UserProfileId",
                table: "ProjectGitManagerGrants",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositories_GitHubRepositoryId_AccessStatus_Unli~",
                table: "ProjectGitRepositories",
                columns: new[] { "GitHubRepositoryId", "AccessStatus", "UnlinkedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositories_LinkedByProfileId",
                table: "ProjectGitRepositories",
                column: "LinkedByProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositories_OAuthConnectionId",
                table: "ProjectGitRepositories",
                column: "OAuthConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositories_ProjectId_GitHubRepositoryId",
                table: "ProjectGitRepositories",
                columns: new[] { "ProjectId", "GitHubRepositoryId" },
                unique: true,
                filter: "\"UnlinkedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositoryHistory_ActorProfileId",
                table: "ProjectGitRepositoryHistory",
                column: "ActorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositoryHistory_NewOAuthConnectionId",
                table: "ProjectGitRepositoryHistory",
                column: "NewOAuthConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositoryHistory_PreviousOAuthConnectionId",
                table: "ProjectGitRepositoryHistory",
                column: "PreviousOAuthConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositoryHistory_ProjectGitRepositoryId_CreatedAt",
                table: "ProjectGitRepositoryHistory",
                columns: new[] { "ProjectGitRepositoryId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubOAuthAuthorizationFlows");

            migrationBuilder.DropTable(
                name: "ProjectGitManagerGrants");

            migrationBuilder.DropTable(
                name: "ProjectGitRepositoryHistory");

            migrationBuilder.DropTable(
                name: "ProjectGitRepositories");

            migrationBuilder.DropTable(
                name: "GitHubOAuthConnections");

            migrationBuilder.DropTable(
                name: "GitHubRepositories");
        }
    }
}
