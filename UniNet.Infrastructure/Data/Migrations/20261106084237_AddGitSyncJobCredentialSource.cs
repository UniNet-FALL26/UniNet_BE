using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitSyncJobCredentialSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OAuthConnectionId",
                table: "GitSyncJobs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<short>(
                name: "CredentialSource",
                table: "GitSyncJobs",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "GitHubAppRepositoryGrantId",
                table: "GitSyncJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitSyncJobs_GitHubAppRepositoryGrantId",
                table: "GitSyncJobs",
                column: "GitHubAppRepositoryGrantId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitSyncJobs_CredentialSource",
                table: "GitSyncJobs",
                sql: "(\"CredentialSource\" = 0 AND \"OAuthConnectionId\" IS NOT NULL AND \"GitHubAppRepositoryGrantId\" IS NULL) OR (\"CredentialSource\" = 1 AND \"OAuthConnectionId\" IS NULL AND \"GitHubAppRepositoryGrantId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_GitSyncJobs_GitHubAppRepositoryGrants_GitHubAppRepositoryGr~",
                table: "GitSyncJobs",
                column: "GitHubAppRepositoryGrantId",
                principalTable: "GitHubAppRepositoryGrants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GitSyncJobs_GitHubAppRepositoryGrants_GitHubAppRepositoryGr~",
                table: "GitSyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_GitSyncJobs_GitHubAppRepositoryGrantId",
                table: "GitSyncJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitSyncJobs_CredentialSource",
                table: "GitSyncJobs");

            migrationBuilder.DropColumn(
                name: "CredentialSource",
                table: "GitSyncJobs");

            migrationBuilder.DropColumn(
                name: "GitHubAppRepositoryGrantId",
                table: "GitSyncJobs");

            migrationBuilder.AlterColumn<Guid>(
                name: "OAuthConnectionId",
                table: "GitSyncJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
