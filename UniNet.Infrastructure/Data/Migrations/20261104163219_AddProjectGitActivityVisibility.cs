using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectGitActivityVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectGitActivityVisibilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitActivityVisibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectGitActivityVisibilities_GitActivities_GitActivityId",
                        column: x => x.GitActivityId,
                        principalTable: "GitActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitActivityVisibilities_ProjectGitRepositories_Proje~",
                        column: x => x.ProjectGitRepositoryId,
                        principalTable: "ProjectGitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitActivityVisibilities_GitActivityId",
                table: "ProjectGitActivityVisibilities",
                column: "GitActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitActivityVisibilities_ProjectGitRepositoryId_GitAc~",
                table: "ProjectGitActivityVisibilities",
                columns: new[] { "ProjectGitRepositoryId", "GitActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitActivityVisibilities_ProjectGitRepositoryId_Obser~",
                table: "ProjectGitActivityVisibilities",
                columns: new[] { "ProjectGitRepositoryId", "ObservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectGitActivityVisibilities");
        }
    }
}
