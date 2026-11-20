using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitActivityVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubProviderObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalObjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    RetrievalMetadataJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubProviderObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubProviderObjects_GitHubRepositories_GitHubRepositoryId",
                        column: x => x.GitHubRepositoryId,
                        principalTable: "GitHubRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubProviderObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitActivities_GitHubProviderObjects_GitHubProviderObjectId",
                        column: x => x.GitHubProviderObjectId,
                        principalTable: "GitHubProviderObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGitActivityGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubProviderObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitActivityGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectGitActivityGrants_GitHubProviderObjects_GitHubProvid~",
                        column: x => x.GitHubProviderObjectId,
                        principalTable: "GitHubProviderObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitActivityGrants_ProjectGitRepositories_ProjectGitR~",
                        column: x => x.ProjectGitRepositoryId,
                        principalTable: "ProjectGitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitActivityActors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Login = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitActivityActors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitActivityActors_GitActivities_GitActivityId",
                        column: x => x.GitActivityId,
                        principalTable: "GitActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitActivities_GitHubProviderObjectId_ActivityType",
                table: "GitActivities",
                columns: new[] { "GitHubProviderObjectId", "ActivityType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitActivities_OccurredAt_Id",
                table: "GitActivities",
                columns: new[] { "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GitActivityActors_GitActivityId_Role_ExternalAccountId",
                table: "GitActivityActors",
                columns: new[] { "GitActivityId", "Role", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubProviderObjects_GitHubRepositoryId_Category_ExternalO~",
                table: "GitHubProviderObjects",
                columns: new[] { "GitHubRepositoryId", "Category", "ExternalObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubProviderObjects_GitHubRepositoryId_OccurredAt",
                table: "GitHubProviderObjects",
                columns: new[] { "GitHubRepositoryId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitActivityGrants_GitHubProviderObjectId",
                table: "ProjectGitActivityGrants",
                column: "GitHubProviderObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitActivityGrants_ProjectGitRepositoryId_GitHubProvi~",
                table: "ProjectGitActivityGrants",
                columns: new[] { "ProjectGitRepositoryId", "GitHubProviderObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitActivityGrants_ProjectGitRepositoryId_ObservedAt",
                table: "ProjectGitActivityGrants",
                columns: new[] { "ProjectGitRepositoryId", "ObservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitActivityActors");

            migrationBuilder.DropTable(
                name: "ProjectGitActivityGrants");

            migrationBuilder.DropTable(
                name: "GitActivities");

            migrationBuilder.DropTable(
                name: "GitHubProviderObjects");
        }
    }
}
