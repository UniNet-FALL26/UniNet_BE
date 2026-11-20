using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectGitEvidenceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectGitRepositories_Id_ProjectId",
                table: "ProjectGitRepositories",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProjectEvidenceObservations_Id_GitEvidenceVersionId_Project~",
                table: "ProjectEvidenceObservations",
                columns: new[] { "Id", "GitEvidenceVersionId", "ProjectGitRepositoryId" });

            migrationBuilder.CreateTable(
                name: "ProjectGitEvidenceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ObservationCutoff = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturePolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    Finalized = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitEvidenceSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_ProjectGitEvidenceSnapshots_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGitEvidenceSnapshotEntries",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectEvidenceObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitEvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectGitRepositoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitEvidenceSnapshotEntries", x => new { x.SnapshotId, x.ProjectEvidenceObservationId });
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotEntries_ProjectEvidenceObservatio~",
                        columns: x => new { x.ProjectEvidenceObservationId, x.GitEvidenceVersionId, x.ProjectGitRepositoryId },
                        principalTable: "ProjectEvidenceObservations",
                        principalColumns: new[] { "Id", "GitEvidenceVersionId", "ProjectGitRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotEntries_ProjectGitEvidenceSnapsho~",
                        columns: x => new { x.SnapshotId, x.ProjectId },
                        principalTable: "ProjectGitEvidenceSnapshots",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitEvidenceSnapshotEntries_ProjectGitRepositories_Pr~",
                        columns: x => new { x.ProjectGitRepositoryId, x.ProjectId },
                        principalTable: "ProjectGitRepositories",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotEntries_ProjectEvidenceObservatio~",
                table: "ProjectGitEvidenceSnapshotEntries",
                columns: new[] { "ProjectEvidenceObservationId", "GitEvidenceVersionId", "ProjectGitRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotEntries_ProjectGitRepositoryId_Pr~",
                table: "ProjectGitEvidenceSnapshotEntries",
                columns: new[] { "ProjectGitRepositoryId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshotEntries_SnapshotId_ProjectId",
                table: "ProjectGitEvidenceSnapshotEntries",
                columns: new[] { "SnapshotId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitEvidenceSnapshots_ProjectId_CreatedAt",
                table: "ProjectGitEvidenceSnapshots",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.Sql("""
                CREATE FUNCTION uninet_snapshot_header_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NEW."Finalized" THEN RAISE EXCEPTION 'snapshot must begin as draft' USING ERRCODE = '55000'; END IF;
                    RETURN NEW;
                  END IF;
                  IF TG_OP = 'UPDATE' AND NOT OLD."Finalized" AND NEW."Finalized"
                    AND (NEW."Id", NEW."ProjectId", NEW."CreatedAt", NEW."ObservationCutoff", NEW."CapturePolicyVersion")
                      IS NOT DISTINCT FROM (OLD."Id", OLD."ProjectId", OLD."CreatedAt", OLD."ObservationCutoff", OLD."CapturePolicyVersion") THEN
                    RETURN NEW;
                  END IF;
                  RAISE EXCEPTION 'snapshot is immutable' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER snapshot_header_guard BEFORE INSERT OR UPDATE OR DELETE ON "ProjectGitEvidenceSnapshots"
                  FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_header_guard();
                CREATE FUNCTION uninet_snapshot_entry_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE is_final boolean;
                BEGIN
                  IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'snapshot entries are immutable' USING ERRCODE = '55000'; END IF;
                  SELECT "Finalized" INTO is_final FROM "ProjectGitEvidenceSnapshots" WHERE "Id" = NEW."SnapshotId" FOR UPDATE;
                  IF NOT FOUND OR is_final THEN RAISE EXCEPTION 'snapshot is not open' USING ERRCODE = '55000'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER snapshot_entry_guard BEFORE INSERT OR UPDATE OR DELETE ON "ProjectGitEvidenceSnapshotEntries"
                  FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_entry_guard();
                CREATE FUNCTION uninet_snapshot_require_finalized() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "ProjectGitEvidenceSnapshots" WHERE "Id" = NEW."Id" AND NOT "Finalized") THEN
                    RAISE EXCEPTION 'snapshot must be finalized before commit' USING ERRCODE = '55000';
                  END IF;
                  RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER snapshot_require_finalized AFTER INSERT OR UPDATE ON "ProjectGitEvidenceSnapshots"
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_require_finalized();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Destructive to snapshot audit history; only run with an approved backup.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS snapshot_entry_guard ON "ProjectGitEvidenceSnapshotEntries";
                DROP TRIGGER IF EXISTS snapshot_header_guard ON "ProjectGitEvidenceSnapshots";
                DROP TRIGGER IF EXISTS snapshot_require_finalized ON "ProjectGitEvidenceSnapshots";
                DROP FUNCTION IF EXISTS uninet_snapshot_entry_guard();
                DROP FUNCTION IF EXISTS uninet_snapshot_header_guard();
                DROP FUNCTION IF EXISTS uninet_snapshot_require_finalized();
                """);
            migrationBuilder.DropTable(
                name: "ProjectGitEvidenceSnapshotEntries");

            migrationBuilder.DropTable(
                name: "ProjectGitEvidenceSnapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectGitRepositories_Id_ProjectId",
                table: "ProjectGitRepositories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProjectEvidenceObservations_Id_GitEvidenceVersionId_Project~",
                table: "ProjectEvidenceObservations");
        }
    }
}
