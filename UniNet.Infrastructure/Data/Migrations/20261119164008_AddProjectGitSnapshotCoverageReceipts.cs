using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectGitSnapshotCoverageReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitSyncJobs_Id_ProjectGitRepositoryId",
                table: "GitSyncJobs",
                columns: new[] { "Id", "ProjectGitRepositoryId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitSyncCheckpoints_Id_SyncJobId",
                table: "GitSyncCheckpoints",
                columns: new[] { "Id", "SyncJobId" });

            migrationBuilder.CreateTable(
                name: "ProjectGitEvidenceSnapshotCoverage",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitSyncCheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitSyncJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ItemsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    ScopeCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JobCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceiptPolicyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitEvidenceSnapshotCoverage", x => new { x.SnapshotId, x.GitSyncCheckpointId });
                    table.CheckConstraint("CK_SnapshotCoverage_Receipt", "\"ItemsProcessed\" >= 0 AND \"ReceiptPolicyVersion\" = 1 AND \"ScopeCompletedAt\" <= \"JobCompletedAt\"");
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotCoverage_GitSyncCheckpoints_GitSy~",
                        columns: x => new { x.GitSyncCheckpointId, x.GitSyncJobId },
                        principalTable: "GitSyncCheckpoints",
                        principalColumns: new[] { "Id", "SyncJobId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotCoverage_GitSyncJobs_GitSyncJobId~",
                        columns: x => new { x.GitSyncJobId, x.ProjectGitRepositoryId },
                        principalTable: "GitSyncJobs",
                        principalColumns: new[] { "Id", "ProjectGitRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotCoverage_ProjectGitEvidenceSnapsh~",
                        columns: x => new { x.SnapshotId, x.ProjectId },
                        principalTable: "ProjectGitEvidenceSnapshots",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotCoverage_ProjectGitRepositories_P~",
                        columns: x => new { x.ProjectGitRepositoryId, x.ProjectId },
                        principalTable: "ProjectGitRepositories",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotCoverage_GitSyncCheckpointId_GitS~",
                table: "ProjectGitEvidenceSnapshotCoverage",
                columns: new[] { "GitSyncCheckpointId", "GitSyncJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotCoverage_GitSyncJobId_ProjectGitR~",
                table: "ProjectGitEvidenceSnapshotCoverage",
                columns: new[] { "GitSyncJobId", "ProjectGitRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotCoverage_ProjectGitRepositoryId_P~",
                table: "ProjectGitEvidenceSnapshotCoverage",
                columns: new[] { "ProjectGitRepositoryId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotCoverage_SnapshotId_GitSyncJobId_~",
                table: "ProjectGitEvidenceSnapshotCoverage",
                columns: new[] { "SnapshotId", "GitSyncJobId", "Category", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotCoverage_SnapshotId_ProjectId",
                table: "ProjectGitEvidenceSnapshotCoverage",
                columns: new[] { "SnapshotId", "ProjectId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION uninet_snapshot_coverage_validate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM "ProjectGitEvidenceSnapshots" s
                    JOIN "ProjectGitRepositories" a ON a."ProjectId" = s."ProjectId"
                    JOIN "GitSyncJobs" j ON j."ProjectGitRepositoryId" = a."Id" AND j."GitHubRepositoryId" = a."GitHubRepositoryId"
                    JOIN "GitSyncCheckpoints" c ON c."SyncJobId" = j."Id"
                    WHERE s."Id" = NEW."SnapshotId" AND s."ProjectId" = NEW."ProjectId"
                      AND s."CapturePolicyVersion" = 2 AND NOT s."Finalized"
                      AND a."Id" = NEW."ProjectGitRepositoryId" AND j."Id" = NEW."GitSyncJobId"
                      AND c."Id" = NEW."GitSyncCheckpointId" AND j."Status" = 2 AND c."Status" = 2
                      AND c."Category" = NEW."Category" AND c."ScopeKey" = NEW."ScopeKey"
                      AND c."ItemsProcessed" = NEW."ItemsProcessed" AND c."LastProcessedAt" = NEW."ScopeCompletedAt"
                      AND j."CompletedAt" = NEW."JobCompletedAt" AND j."CompletedAt" <= s."ObservationCutoff"
                  ) THEN
                    RAISE EXCEPTION 'snapshot coverage requires matching completed retrieval state' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER snapshot_coverage_immutable BEFORE INSERT OR UPDATE OR DELETE ON "ProjectGitEvidenceSnapshotCoverage"
                  FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_entry_guard();
                CREATE TRIGGER snapshot_coverage_validate BEFORE INSERT ON "ProjectGitEvidenceSnapshotCoverage"
                  FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_coverage_validate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "ProjectGitEvidenceSnapshotCoverage") THEN
                    RAISE EXCEPTION 'archive populated snapshot coverage before rollback' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ProjectGitEvidenceSnapshotCoverage");
            migrationBuilder.Sql("DROP FUNCTION uninet_snapshot_coverage_validate();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitSyncJobs_Id_ProjectGitRepositoryId",
                table: "GitSyncJobs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitSyncCheckpoints_Id_SyncJobId",
                table: "GitSyncCheckpoints");
        }
    }
}
