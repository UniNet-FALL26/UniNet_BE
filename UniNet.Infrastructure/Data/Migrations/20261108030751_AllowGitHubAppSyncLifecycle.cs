using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowGitHubAppSyncLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectGitRepositories_CredentialSource",
                table: "ProjectGitRepositories");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectGitRepositories_CredentialSource",
                table: "ProjectGitRepositories",
                sql: "(\"CredentialSource\" = 0 AND \"OAuthConnectionId\" IS NOT NULL AND \"GitHubAppRepositoryGrantId\" IS NULL) OR (\"CredentialSource\" = 1 AND \"OAuthConnectionId\" IS NULL AND \"GitHubAppRepositoryGrantId\" IS NOT NULL AND ((\"SyncPaused\" = TRUE AND \"AccessStatus\" = 0) OR \"AccessStatus\" IN (1, 2)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectGitRepositories_CredentialSource",
                table: "ProjectGitRepositories");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectGitRepositories_CredentialSource",
                table: "ProjectGitRepositories",
                sql: "(\"CredentialSource\" = 0 AND \"OAuthConnectionId\" IS NOT NULL AND \"GitHubAppRepositoryGrantId\" IS NULL) OR (\"CredentialSource\" = 1 AND \"OAuthConnectionId\" IS NULL AND \"GitHubAppRepositoryGrantId\" IS NOT NULL AND ((\"SyncPaused\" = TRUE AND \"AccessStatus\" = 0) OR (\"SyncPaused\" = FALSE AND \"AccessStatus\" = 1)))");
        }
    }
}
