using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitRepositoryCredentialSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OAuthConnectionId",
                table: "ProjectGitRepositories",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<short>(
                name: "CredentialSource",
                table: "ProjectGitRepositories",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "GitHubAppRepositoryGrantId",
                table: "ProjectGitRepositories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitRepositories_GitHubAppRepositoryGrantId",
                table: "ProjectGitRepositories",
                column: "GitHubAppRepositoryGrantId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectGitRepositories_CredentialSource",
                table: "ProjectGitRepositories",
                sql: "(\"CredentialSource\" = 0 AND \"OAuthConnectionId\" IS NOT NULL AND \"GitHubAppRepositoryGrantId\" IS NULL) OR (\"CredentialSource\" = 1 AND \"OAuthConnectionId\" IS NULL AND \"GitHubAppRepositoryGrantId\" IS NOT NULL AND \"SyncPaused\" = TRUE AND \"AccessStatus\" = 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectGitRepositories_GitHubAppRepositoryGrants_GitHubAppR~",
                table: "ProjectGitRepositories",
                column: "GitHubAppRepositoryGrantId",
                principalTable: "GitHubAppRepositoryGrants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectGitRepositories_GitHubAppRepositoryGrants_GitHubAppR~",
                table: "ProjectGitRepositories");

            migrationBuilder.DropIndex(
                name: "IX_ProjectGitRepositories_GitHubAppRepositoryGrantId",
                table: "ProjectGitRepositories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectGitRepositories_CredentialSource",
                table: "ProjectGitRepositories");

            migrationBuilder.DropColumn(
                name: "CredentialSource",
                table: "ProjectGitRepositories");

            migrationBuilder.DropColumn(
                name: "GitHubAppRepositoryGrantId",
                table: "ProjectGitRepositories");

            migrationBuilder.AlterColumn<Guid>(
                name: "OAuthConnectionId",
                table: "ProjectGitRepositories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
