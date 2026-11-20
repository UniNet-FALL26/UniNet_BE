using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitCommitBranchReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GitActivityActors_GitActivityId_Role_ExternalAccountId",
                table: "GitActivityActors");

            migrationBuilder.AddColumn<string>(
                name: "ActorKey",
                table: "GitActivityActors",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "GitCommitBranchReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubProviderObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitCommitBranchReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitCommitBranchReferences_GitHubProviderObjects_GitHubProvi~",
                        column: x => x.GitHubProviderObjectId,
                        principalTable: "GitHubProviderObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitActivityActors_GitActivityId_ActorKey",
                table: "GitActivityActors",
                columns: new[] { "GitActivityId", "ActorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitCommitBranchReferences_GitHubProviderObjectId_BranchName",
                table: "GitCommitBranchReferences",
                columns: new[] { "GitHubProviderObjectId", "BranchName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitCommitBranchReferences");

            migrationBuilder.DropIndex(
                name: "IX_GitActivityActors_GitActivityId_ActorKey",
                table: "GitActivityActors");

            migrationBuilder.DropColumn(
                name: "ActorKey",
                table: "GitActivityActors");

            migrationBuilder.CreateIndex(
                name: "IX_GitActivityActors_GitActivityId_Role_ExternalAccountId",
                table: "GitActivityActors",
                columns: new[] { "GitActivityId", "Role", "ExternalAccountId" },
                unique: true);
        }
    }
}
