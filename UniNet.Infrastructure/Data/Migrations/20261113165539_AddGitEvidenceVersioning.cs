using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitEvidenceVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitEvidenceVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubProviderObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CanonicalizationVersion = table.Column<int>(type: "integer", nullable: false),
                    HashAlgorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FrozenFactsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitEvidenceVersions", x => x.Id);
                    table.CheckConstraint("CK_GitEvidenceVersions_Hash", "\"HashAlgorithm\" = 'SHA-256' AND length(\"ContentHash\") = 64");
                    table.CheckConstraint("CK_GitEvidenceVersions_Versions", "\"EvidenceSchemaVersion\" > 0 AND \"CanonicalizationVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_GitEvidenceVersions_GitHubProviderObjects_GitHubProviderObj~",
                        column: x => x.GitHubProviderObjectId,
                        principalTable: "GitHubProviderObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectEvidenceObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitSyncJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PageCursor = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AuthorizationSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RetrievalCompleteness = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEvidenceObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceObservations_GitEvidenceVersions_GitEvidence~",
                        column: x => x.GitEvidenceVersionId,
                        principalTable: "GitEvidenceVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceObservations_GitSyncJobs_GitSyncJobId",
                        column: x => x.GitSyncJobId,
                        principalTable: "GitSyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceObservations_ProjectGitRepositories_ProjectG~",
                        column: x => x.ProjectGitRepositoryId,
                        principalTable: "ProjectGitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitEvidenceVersions_GitHubProviderObjectId_EvidenceSchemaVe~",
                table: "GitEvidenceVersions",
                columns: new[] { "GitHubProviderObjectId", "EvidenceSchemaVersion", "CanonicalizationVersion", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_GitEvidenceVersionId",
                table: "ProjectEvidenceObservations",
                column: "GitEvidenceVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_GitSyncJobId",
                table: "ProjectEvidenceObservations",
                column: "GitSyncJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_ProjectGitRepositoryId_GitSyncJ~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "ProjectGitRepositoryId", "GitSyncJobId", "Category", "ScopeKey", "GitEvidenceVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceObservations_ProjectGitRepositoryId_Observed~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "ProjectGitRepositoryId", "ObservedAt" });

            migrationBuilder.Sql("""
                CREATE FUNCTION uninet_reject_git_evidence_version_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN
                  RAISE EXCEPTION 'Git evidence versions are immutable' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER git_evidence_versions_immutable
                BEFORE UPDATE OR DELETE ON "GitEvidenceVersions"
                FOR EACH ROW EXECUTE FUNCTION uninet_reject_git_evidence_version_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Destructive to evidence history; never execute without an approved backup and rollback plan.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS git_evidence_versions_immutable ON \"GitEvidenceVersions\"; DROP FUNCTION IF EXISTS uninet_reject_git_evidence_version_mutation();");
            migrationBuilder.DropTable(
                name: "ProjectEvidenceObservations");

            migrationBuilder.DropTable(
                name: "GitEvidenceVersions");
        }
    }
}
