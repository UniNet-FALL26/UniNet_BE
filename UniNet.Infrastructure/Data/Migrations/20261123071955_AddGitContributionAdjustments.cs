using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitContributionAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitContributionAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutionDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GitSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeerCohortId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    BasePublishedRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitContributionAdjustments", x => x.Id);
                    table.UniqueConstraint("AK_GitContributionAdjustments_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_GitAdjustment_State", "\"State\" IN ('Syncing','GitCaptured','PeerOpen','DraftCreated','Published','Failed')");
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustments_ExperimentalPeerCohorts_PeerCoho~",
                        column: x => x.PeerCohortId,
                        principalTable: "ExperimentalPeerCohorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustments_OfficialContributionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OfficialContributionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustments_ProjectGitEvidenceSnapshots_GitS~",
                        column: x => x.GitSnapshotId,
                        principalTable: "ProjectGitEvidenceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustments_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitContributionAdjustmentRepositories",
                columns: table => new
                {
                    AdjustmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitContributionAdjustmentRepositories", x => new { x.AdjustmentId, x.AssociationId });
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustmentRepositories_GitContributionAdjust~",
                        columns: x => new { x.AdjustmentId, x.ProjectId },
                        principalTable: "GitContributionAdjustments",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionAdjustmentRepositories_GitSyncJobs_JobId_Ass~",
                        columns: x => new { x.JobId, x.AssociationId, x.RepositoryId },
                        principalTable: "GitSyncJobs",
                        principalColumns: new[] { "Id", "ProjectGitRepositoryId", "GitHubRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustmentRepositories_AdjustmentId_ProjectId",
                table: "GitContributionAdjustmentRepositories",
                columns: new[] { "AdjustmentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustmentRepositories_JobId",
                table: "GitContributionAdjustmentRepositories",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustmentRepositories_JobId_AssociationId_R~",
                table: "GitContributionAdjustmentRepositories",
                columns: new[] { "JobId", "AssociationId", "RepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustments_CreatedById",
                table: "GitContributionAdjustments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustments_GitSnapshotId",
                table: "GitContributionAdjustments",
                column: "GitSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustments_PeerCohortId",
                table: "GitContributionAdjustments",
                column: "PeerCohortId");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustments_ProjectId",
                table: "GitContributionAdjustments",
                column: "ProjectId",
                unique: true,
                filter: "\"State\" NOT IN ('Published','Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustments_ProjectId_RequestId",
                table: "GitContributionAdjustments",
                columns: new[] { "ProjectId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionAdjustments_RunId",
                table: "GitContributionAdjustments",
                column: "RunId");
            migrationBuilder.Sql("""
                CREATE FUNCTION uninet_adjustment_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Adjustment audit is retained' USING ERRCODE='55000'; END IF;
                  IF TG_TABLE_NAME='GitContributionAdjustmentRepositories' THEN
                    IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Adjustment scope is immutable' USING ERRCODE='55000'; END IF;
                    IF NOT EXISTS(SELECT 1 FROM "GitSyncJobs" j JOIN "ProjectGitRepositories" l ON l."Id"=j."ProjectGitRepositoryId"
                      JOIN "GitContributionAdjustments" a ON a."Id"=NEW."AdjustmentId"
                      WHERE j."Id"=NEW."JobId" AND l."Id"=NEW."AssociationId" AND l."ProjectId"=NEW."ProjectId"
                        AND a."ProjectId"=NEW."ProjectId" AND j."SyncType"=3 AND j."CreatedAt">=a."CreatedAt") THEN
                      RAISE EXCEPTION 'Invalid adjustment job correlation' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF TG_OP='UPDATE' AND (
                    ROW(NEW."Id",NEW."ProjectId",NEW."RequestId",NEW."CreatedById",NEW."CreatedAt",NEW."ExecutionDeadline",NEW."BasePublishedRunId")
                      IS DISTINCT FROM ROW(OLD."Id",OLD."ProjectId",OLD."RequestId",OLD."CreatedById",OLD."CreatedAt",OLD."ExecutionDeadline",OLD."BasePublishedRunId")
                    OR OLD."GitSnapshotId" IS NOT NULL AND NEW."GitSnapshotId" IS DISTINCT FROM OLD."GitSnapshotId"
                    OR OLD."PeerCohortId" IS NOT NULL AND NEW."PeerCohortId" IS DISTINCT FROM OLD."PeerCohortId"
                    OR OLD."RunId" IS NOT NULL AND NEW."RunId" IS DISTINCT FROM OLD."RunId"
                    OR NEW."Revision"<>OLD."Revision"+1) THEN
                      RAISE EXCEPTION 'Adjustment correlation is immutable' USING ERRCODE='55000';
                  END IF;
                  IF NEW."GitSnapshotId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProjectGitEvidenceSnapshots" s WHERE s."Id"=NEW."GitSnapshotId" AND s."ProjectId"=NEW."ProjectId" AND s."Finalized" AND s."CapturePolicyVersion"=2 AND s."CreatedAt">=NEW."CreatedAt") THEN
                    RAISE EXCEPTION 'Invalid adjustment snapshot' USING ERRCODE='23514';
                  END IF;
                  IF NEW."PeerCohortId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ExperimentalPeerCohorts" c WHERE c."Id"=NEW."PeerCohortId" AND c."ProjectId"=NEW."ProjectId" AND c."CreatedAt">=NEW."CreatedAt") THEN
                    RAISE EXCEPTION 'Invalid adjustment peer round' USING ERRCODE='23514';
                  END IF;
                  IF NEW."RunId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" r JOIN "GitContributionRuns" g ON g."Id"=r."Id" JOIN "ExperimentalPeerSnapshots" p ON p."Id"=g."PeerSnapshotId" WHERE r."Id"=NEW."RunId" AND r."ProjectId"=NEW."ProjectId" AND g."GitEvidenceSnapshotId"=NEW."GitSnapshotId" AND p."CohortId"=NEW."PeerCohortId") THEN
                    RAISE EXCEPTION 'Invalid adjustment run' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER adjustment_guard BEFORE INSERT OR UPDATE OR DELETE ON "GitContributionAdjustments" FOR EACH ROW EXECUTE FUNCTION uninet_adjustment_guard();
                CREATE TRIGGER adjustment_scope_guard BEFORE INSERT OR UPDATE OR DELETE ON "GitContributionAdjustmentRepositories" FOR EACH ROW EXECUTE FUNCTION uninet_adjustment_guard();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "GitContributionAdjustments") THEN
                    RAISE EXCEPTION 'Adjustment history exists; destructive downgrade refused' USING ERRCODE='55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.Sql("DROP TRIGGER adjustment_guard ON \"GitContributionAdjustments\"; DROP TRIGGER adjustment_scope_guard ON \"GitContributionAdjustmentRepositories\"; DROP FUNCTION uninet_adjustment_guard();");
            migrationBuilder.DropTable(
                name: "GitContributionAdjustmentRepositories");

            migrationBuilder.DropTable(
                name: "GitContributionAdjustments");
        }
    }
}
