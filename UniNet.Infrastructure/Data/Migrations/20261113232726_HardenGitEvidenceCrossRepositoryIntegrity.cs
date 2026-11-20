using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenGitEvidenceCrossRepositoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectEvidenceObservations_GitEvidenceVersionId",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectEvidenceObservations_GitSyncJobId",
                table: "ProjectEvidenceObservations");

            migrationBuilder.AddColumn<Guid>(
                name: "GitHubRepositoryId",
                table: "ProjectEvidenceObservations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "GitHubRepositoryId",
                table: "GitEvidenceVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Derive only the new redundant keys. Fail before updating observations if
            // existing records do not already satisfy the relationships being enforced.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM "ProjectEvidenceObservations" o
                    JOIN "GitSyncJobs" j ON j."Id" = o."GitSyncJobId"
                    JOIN "ProjectGitRepositories" a ON a."Id" = o."ProjectGitRepositoryId"
                    JOIN "GitEvidenceVersions" v ON v."Id" = o."GitEvidenceVersionId"
                    JOIN "GitHubProviderObjects" p ON p."Id" = v."GitHubProviderObjectId"
                    WHERE j."ProjectGitRepositoryId" <> a."Id"
                       OR j."GitHubRepositoryId" <> a."GitHubRepositoryId"
                       OR p."GitHubRepositoryId" <> a."GitHubRepositoryId"
                  ) THEN RAISE EXCEPTION 'Existing evidence observations have inconsistent repository relationships'; END IF;
                END $$;
                ALTER TABLE "GitEvidenceVersions" DISABLE TRIGGER git_evidence_versions_immutable;
                UPDATE "GitEvidenceVersions" v SET "GitHubRepositoryId" = p."GitHubRepositoryId"
                FROM "GitHubProviderObjects" p WHERE p."Id" = v."GitHubProviderObjectId";
                ALTER TABLE "GitEvidenceVersions" ENABLE TRIGGER git_evidence_versions_immutable;
                UPDATE "ProjectEvidenceObservations" o SET "GitHubRepositoryId" = a."GitHubRepositoryId"
                FROM "ProjectGitRepositories" a WHERE a."Id" = o."ProjectGitRepositoryId";
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectGitRepositories_Id_GitHubRepositoryId",
                table: "ProjectGitRepositories",
                columns: new[] { "Id", "GitHubRepositoryId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitSyncJobs_Id_ProjectGitRepositoryId_GitHubRepositoryId",
                table: "GitSyncJobs",
                columns: new[] { "Id", "ProjectGitRepositoryId", "GitHubRepositoryId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitHubProviderObjects_Id_GitHubRepositoryId",
                table: "GitHubProviderObjects",
                columns: new[] { "Id", "GitHubRepositoryId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitEvidenceVersions_Id_GitHubRepositoryId",
                table: "GitEvidenceVersions",
                columns: new[] { "Id", "GitHubRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_GitEvidenceVersionId_GitHubRepo~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "GitEvidenceVersionId", "GitHubRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_GitSyncJobId_ProjectGitReposito~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "GitSyncJobId", "ProjectGitRepositoryId", "GitHubRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_ProjectGitRepositoryId_GitHubRe~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "ProjectGitRepositoryId", "GitHubRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitEvidenceVersions_GitHubProviderObjectId_GitHubRepository~",
                table: "GitEvidenceVersions",
                columns: new[] { "GitHubProviderObjectId", "GitHubRepositoryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_GitEvidenceVersions_GitHubProviderObjects_GitHubProviderOb~1",
                table: "GitEvidenceVersions",
                columns: new[] { "GitHubProviderObjectId", "GitHubRepositoryId" },
                principalTable: "GitHubProviderObjects",
                principalColumns: new[] { "Id", "GitHubRepositoryId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectEvidenceObservations_GitEvidenceVersions_GitEvidenc~1",
                table: "ProjectEvidenceObservations",
                columns: new[] { "GitEvidenceVersionId", "GitHubRepositoryId" },
                principalTable: "GitEvidenceVersions",
                principalColumns: new[] { "Id", "GitHubRepositoryId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectEvidenceObservations_GitSyncJobs_GitSyncJobId_Projec~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "GitSyncJobId", "ProjectGitRepositoryId", "GitHubRepositoryId" },
                principalTable: "GitSyncJobs",
                principalColumns: new[] { "Id", "ProjectGitRepositoryId", "GitHubRepositoryId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectEvidenceObservations_ProjectGitRepositories_Project~1",
                table: "ProjectEvidenceObservations",
                columns: new[] { "ProjectGitRepositoryId", "GitHubRepositoryId" },
                principalTable: "ProjectGitRepositories",
                principalColumns: new[] { "Id", "GitHubRepositoryId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GitEvidenceVersions_GitHubProviderObjects_GitHubProviderOb~1",
                table: "GitEvidenceVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectEvidenceObservations_GitEvidenceVersions_GitEvidenc~1",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectEvidenceObservations_GitSyncJobs_GitSyncJobId_Projec~",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectEvidenceObservations_ProjectGitRepositories_Project~1",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectGitRepositories_Id_GitHubRepositoryId",
                table: "ProjectGitRepositories");

            migrationBuilder.DropIndex(
                name: "IX_ProjectEvidenceObservations_GitEvidenceVersionId_GitHubRepo~",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectEvidenceObservations_GitSyncJobId_ProjectGitReposito~",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropIndex(
                name: "IX_ProjectEvidenceObservations_ProjectGitRepositoryId_GitHubRe~",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitSyncJobs_Id_ProjectGitRepositoryId_GitHubRepositoryId",
                table: "GitSyncJobs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitHubProviderObjects_Id_GitHubRepositoryId",
                table: "GitHubProviderObjects");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitEvidenceVersions_Id_GitHubRepositoryId",
                table: "GitEvidenceVersions");

            migrationBuilder.DropIndex(
                name: "IX_GitEvidenceVersions_GitHubProviderObjectId_GitHubRepository~",
                table: "GitEvidenceVersions");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoryId",
                table: "ProjectEvidenceObservations");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoryId",
                table: "GitEvidenceVersions");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_GitEvidenceVersionId",
                table: "ProjectEvidenceObservations",
                column: "GitEvidenceVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_GitSyncJobId",
                table: "ProjectEvidenceObservations",
                column: "GitSyncJobId");
        }
    }
}
