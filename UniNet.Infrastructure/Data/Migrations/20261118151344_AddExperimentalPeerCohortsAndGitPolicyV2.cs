using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentalPeerCohortsAndGitPolicyV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~",
                table: "GitContributionRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitContributionRuns_Policy",
                table: "GitContributionRuns");

            migrationBuilder.AddColumn<string>(
                name: "CalculationContextHash",
                table: "GitContributionRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationStatus",
                table: "GitContributionRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizationPolicyVersion",
                table: "GitContributionRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedResultsJson",
                table: "GitContributionRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PeerSnapshotId",
                table: "GitContributionRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExperimentalPeerCohorts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RosterJson = table.Column<string>(type: "jsonb", nullable: false),
                    RosterHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    OpenedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentalPeerCohorts", x => x.Id);
                    table.UniqueConstraint("AK_ExperimentalPeerCohorts_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_ExperimentalPeerCohorts_Closed", "(\"Status\" = 3) = (\"ClosedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_ExperimentalPeerCohorts_Roster", "jsonb_typeof(\"RosterJson\") = 'array' AND jsonb_array_length(\"RosterJson\") > 0 AND length(\"RosterHash\") = 64");
                    table.CheckConstraint("CK_ExperimentalPeerCohorts_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerCohorts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerCohorts_Users_OpenedById",
                        column: x => x.OpenedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentalPeerSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputJson = table.Column<string>(type: "jsonb", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScoresJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentalPeerSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_ExperimentalPeerSnapshots_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_ExperimentalPeerSnapshots_Hash", "length(\"InputHash\") = 64");
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerSnapshots_ExperimentalPeerCohorts_CohortId_~",
                        columns: x => new { x.CohortId, x.ProjectId },
                        principalTable: "ExperimentalPeerCohorts",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentalPeerSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluateeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentalPeerSubmissions", x => x.Id);
                    table.CheckConstraint("CK_ExperimentalPeerSubmissions_Score", "\"Score\" BETWEEN 1 AND 5 AND \"Revision\" > 0 AND \"EvaluatorId\" <> \"EvaluateeId\"");
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerSubmissions_ExperimentalPeerCohorts_CohortId",
                        column: x => x.CohortId,
                        principalTable: "ExperimentalPeerCohorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerSubmissions_Users_EvaluateeId",
                        column: x => x.EvaluateeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerSubmissions_Users_EvaluatorId",
                        column: x => x.EvaluatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentalPeerRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PreviousScore = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    PreviousComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentalPeerRevisions", x => x.Id);
                    table.CheckConstraint("CK_ExperimentalPeerRevisions_Score", "\"Score\" BETWEEN 1 AND 5 AND (\"PreviousScore\" IS NULL OR \"PreviousScore\" BETWEEN 1 AND 5) AND \"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_ExperimentalPeerRevisions_ExperimentalPeerSubmissions_Submi~",
                        column: x => x.SubmissionId,
                        principalTable: "ExperimentalPeerSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_PeerSnapshotId_ProjectId",
                table: "GitContributionRuns",
                columns: new[] { "PeerSnapshotId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_ProjectId_CalculationContextHash",
                table: "GitContributionRuns",
                columns: new[] { "ProjectId", "CalculationContextHash" },
                unique: true,
                filter: "\"CalculationContextHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~",
                table: "GitContributionRuns",
                columns: new[] { "ProjectId", "GitEvidenceSnapshotId", "ScoringPolicyVersion" },
                unique: true,
                filter: "\"ScoringPolicyVersion\" = 'ExperimentalGitScoringV1'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitContributionRuns_Policy",
                table: "GitContributionRuns",
                sql: "\"ScoringPolicyVersion\" IN ('ExperimentalGitScoringV1','ExperimentalGitScoringV2')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitContributionRuns_V2Context",
                table: "GitContributionRuns",
                sql: "\"ScoringPolicyVersion\" <> 'ExperimentalGitScoringV2' OR length(\"CalculationContextHash\") = 64 AND \"CalculationContextHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerCohorts_OpenedById",
                table: "ExperimentalPeerCohorts",
                column: "OpenedById");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerCohorts_ProjectId",
                table: "ExperimentalPeerCohorts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerRevisions_SubmissionId_Revision",
                table: "ExperimentalPeerRevisions",
                columns: new[] { "SubmissionId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerSnapshots_CohortId",
                table: "ExperimentalPeerSnapshots",
                column: "CohortId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerSnapshots_CohortId_ProjectId",
                table: "ExperimentalPeerSnapshots",
                columns: new[] { "CohortId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerSubmissions_CohortId_EvaluatorId_EvaluateeId",
                table: "ExperimentalPeerSubmissions",
                columns: new[] { "CohortId", "EvaluatorId", "EvaluateeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerSubmissions_EvaluateeId",
                table: "ExperimentalPeerSubmissions",
                column: "EvaluateeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentalPeerSubmissions_EvaluatorId",
                table: "ExperimentalPeerSubmissions",
                column: "EvaluatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_GitContributionRuns_ExperimentalPeerSnapshots_PeerSnapshotI~",
                table: "GitContributionRuns",
                columns: new[] { "PeerSnapshotId", "ProjectId" },
                principalTable: "ExperimentalPeerSnapshots",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION uninet_peer_cohort_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE required_count integer; actual_count integer;
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Peer audit cannot be deleted' USING ERRCODE='55000'; END IF;
                  IF TG_OP = 'INSERT' THEN
                    IF NEW."Status" = 3 THEN RAISE EXCEPTION 'Cohort must begin open' USING ERRCODE='55000'; END IF;
                    IF (SELECT count(DISTINCT value) FROM jsonb_array_elements_text(NEW."RosterJson")) <> jsonb_array_length(NEW."RosterJson") THEN
                      RAISE EXCEPTION 'Duplicate cohort participant' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF OLD."Status"=3 OR (NEW."Id",NEW."ProjectId",NEW."RosterJson",NEW."RosterHash",NEW."CreatedAt",NEW."OpenedById") IS DISTINCT FROM
                    (OLD."Id",OLD."ProjectId",OLD."RosterJson",OLD."RosterHash",OLD."CreatedAt",OLD."OpenedById") THEN
                    RAISE EXCEPTION 'Cohort roster and closed cohort are immutable' USING ERRCODE='55000';
                  END IF;
                  IF NEW."Status"=3 THEN
                    required_count := jsonb_array_length(OLD."RosterJson") * (jsonb_array_length(OLD."RosterJson")-1);
                    SELECT count(*) INTO actual_count FROM "ExperimentalPeerSubmissions" WHERE "CohortId"=OLD."Id";
                    IF actual_count<>required_count OR NOT EXISTS(SELECT 1 FROM "ExperimentalPeerSnapshots" WHERE "CohortId"=OLD."Id") THEN
                      RAISE EXCEPTION 'Complete ratings and peer snapshot required' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER peer_cohort_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExperimentalPeerCohorts" FOR EACH ROW EXECUTE FUNCTION uninet_peer_cohort_guard();

                CREATE FUNCTION uninet_peer_submission_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE c "ExperimentalPeerCohorts"%ROWTYPE;
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Peer audit cannot be deleted' USING ERRCODE='55000'; END IF;
                  SELECT * INTO c FROM "ExperimentalPeerCohorts" WHERE "Id"=NEW."CohortId" FOR UPDATE;
                  IF NOT FOUND OR c."Status"=3 THEN RAISE EXCEPTION 'Peer cohort is closed or missing' USING ERRCODE='55000'; END IF;
                  IF NOT (c."RosterJson" ? NEW."EvaluatorId"::text) OR NOT (c."RosterJson" ? NEW."EvaluateeId"::text) THEN
                    RAISE EXCEPTION 'Peer users must be cohort participants' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP='UPDATE' AND ((NEW."Id",NEW."CohortId",NEW."EvaluatorId",NEW."EvaluateeId",NEW."SubmittedAt") IS DISTINCT FROM
                    (OLD."Id",OLD."CohortId",OLD."EvaluatorId",OLD."EvaluateeId",OLD."SubmittedAt") OR NEW."Revision"<>OLD."Revision"+1) THEN
                    RAISE EXCEPTION 'Peer identity or revision invalid' USING ERRCODE='23514';
                  END IF;
                  IF TG_OP='INSERT' AND NEW."Revision"<>1 THEN RAISE EXCEPTION 'Initial peer revision must be one' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER peer_submission_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExperimentalPeerSubmissions" FOR EACH ROW EXECUTE FUNCTION uninet_peer_submission_guard();
                CREATE FUNCTION uninet_peer_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Peer snapshot/history is immutable' USING ERRCODE='55000'; END $$;
                CREATE TRIGGER peer_snapshot_immutable BEFORE UPDATE OR DELETE ON "ExperimentalPeerSnapshots" FOR EACH ROW EXECUTE FUNCTION uninet_peer_append_only();
                CREATE TRIGGER peer_history_immutable BEFORE UPDATE OR DELETE ON "ExperimentalPeerRevisions" FOR EACH ROW EXECUTE FUNCTION uninet_peer_append_only();
                CREATE FUNCTION uninet_peer_history_insert_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE current_revision integer; cohort_status smallint;
                BEGIN
                  SELECT s."Revision",c."Status" INTO current_revision,cohort_status FROM "ExperimentalPeerSubmissions" s JOIN "ExperimentalPeerCohorts" c ON c."Id"=s."CohortId"
                    WHERE s."Id"=NEW."SubmissionId" FOR UPDATE OF c;
                  IF NOT FOUND OR cohort_status=3 OR current_revision<>NEW."Revision" THEN RAISE EXCEPTION 'Peer history revision invalid or cohort closed' USING ERRCODE='55000'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER peer_history_insert_guard BEFORE INSERT ON "ExperimentalPeerRevisions" FOR EACH ROW EXECUTE FUNCTION uninet_peer_history_insert_guard();
                CREATE FUNCTION uninet_peer_snapshot_insert_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE c "ExperimentalPeerCohorts"%ROWTYPE; actual_count integer;
                BEGIN
                  SELECT * INTO c FROM "ExperimentalPeerCohorts" WHERE "Id"=NEW."CohortId" FOR UPDATE;
                  IF NOT FOUND OR c."Status"<>2 OR c."ProjectId"<>NEW."ProjectId" THEN RAISE EXCEPTION 'Peer cohort is not ready' USING ERRCODE='23514'; END IF;
                  SELECT count(*) INTO actual_count FROM "ExperimentalPeerSubmissions" WHERE "CohortId"=c."Id";
                  IF actual_count<>jsonb_array_length(c."RosterJson")*(jsonb_array_length(c."RosterJson")-1) THEN RAISE EXCEPTION 'Peer ratings incomplete' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER peer_snapshot_insert_guard BEFORE INSERT ON "ExperimentalPeerSnapshots" FOR EACH ROW EXECUTE FUNCTION uninet_peer_snapshot_insert_guard();
                CREATE FUNCTION uninet_peer_snapshot_must_close() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS(SELECT 1 FROM "ExperimentalPeerCohorts" WHERE "Id"=NEW."CohortId" AND "Status"=3) THEN
                    RAISE EXCEPTION 'Peer snapshot requires atomic cohort close' USING ERRCODE='23514';
                  END IF; RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER peer_snapshot_must_close AFTER INSERT ON "ExperimentalPeerSnapshots" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION uninet_peer_snapshot_must_close();
                CREATE FUNCTION uninet_git_v2_inputs_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD."ScoringPolicyVersion"='ExperimentalGitScoringV2' AND
                    (NEW."ProjectId",NEW."GitEvidenceSnapshotId",NEW."ScoringPolicyVersion",NEW."InputSnapshotJson",NEW."InputHash",NEW."CategoryStatusJson",NEW."ResultsJson",NEW."ExcludedActivitiesJson",NEW."PeerSnapshotId",NEW."CalculationContextHash",NEW."NormalizationPolicyVersion",NEW."CalculationStatus",NEW."NormalizedResultsJson") IS DISTINCT FROM
                    (OLD."ProjectId",OLD."GitEvidenceSnapshotId",OLD."ScoringPolicyVersion",OLD."InputSnapshotJson",OLD."InputHash",OLD."CategoryStatusJson",OLD."ResultsJson",OLD."ExcludedActivitiesJson",OLD."PeerSnapshotId",OLD."CalculationContextHash",OLD."NormalizationPolicyVersion",OLD."CalculationStatus",OLD."NormalizedResultsJson") THEN
                    RAISE EXCEPTION 'V2 calculation inputs/results are immutable; create a new run' USING ERRCODE='55000';
                  END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER git_v2_inputs_immutable BEFORE UPDATE ON "GitContributionRuns" FOR EACH ROW EXECUTE FUNCTION uninet_git_v2_inputs_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Audit retention: fail closed rather than silently removing peer or V2 history.
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS(SELECT 1 FROM "ExperimentalPeerCohorts") OR EXISTS(SELECT 1 FROM "GitContributionRuns" WHERE "ScoringPolicyVersion"='ExperimentalGitScoringV2') THEN
                  RAISE EXCEPTION 'Backup and explicit archival plan required before destructive peer rollback' USING ERRCODE='55000'; END IF; END $$;
                DROP TRIGGER peer_snapshot_must_close ON "ExperimentalPeerSnapshots";
                DROP TRIGGER git_v2_inputs_immutable ON "GitContributionRuns";
                DROP TRIGGER peer_snapshot_insert_guard ON "ExperimentalPeerSnapshots";
                DROP TRIGGER peer_snapshot_immutable ON "ExperimentalPeerSnapshots";
                DROP TRIGGER peer_history_immutable ON "ExperimentalPeerRevisions";
                DROP TRIGGER peer_history_insert_guard ON "ExperimentalPeerRevisions";
                DROP TRIGGER peer_submission_guard ON "ExperimentalPeerSubmissions";
                DROP TRIGGER peer_cohort_guard ON "ExperimentalPeerCohorts";
                DROP FUNCTION uninet_peer_snapshot_must_close(); DROP FUNCTION uninet_peer_snapshot_insert_guard();
                DROP FUNCTION uninet_peer_append_only(); DROP FUNCTION uninet_peer_submission_guard(); DROP FUNCTION uninet_peer_cohort_guard();
                DROP FUNCTION uninet_peer_history_insert_guard();
                DROP FUNCTION uninet_git_v2_inputs_immutable();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_GitContributionRuns_ExperimentalPeerSnapshots_PeerSnapshotI~",
                table: "GitContributionRuns");

            migrationBuilder.DropTable(
                name: "ExperimentalPeerRevisions");

            migrationBuilder.DropTable(
                name: "ExperimentalPeerSnapshots");

            migrationBuilder.DropTable(
                name: "ExperimentalPeerSubmissions");

            migrationBuilder.DropTable(
                name: "ExperimentalPeerCohorts");

            migrationBuilder.DropIndex(
                name: "IX_GitContributionRuns_PeerSnapshotId_ProjectId",
                table: "GitContributionRuns");

            migrationBuilder.DropIndex(
                name: "IX_GitContributionRuns_ProjectId_CalculationContextHash",
                table: "GitContributionRuns");

            migrationBuilder.DropIndex(
                name: "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~",
                table: "GitContributionRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitContributionRuns_Policy",
                table: "GitContributionRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitContributionRuns_V2Context",
                table: "GitContributionRuns");

            migrationBuilder.DropColumn(
                name: "CalculationContextHash",
                table: "GitContributionRuns");

            migrationBuilder.DropColumn(
                name: "CalculationStatus",
                table: "GitContributionRuns");

            migrationBuilder.DropColumn(
                name: "NormalizationPolicyVersion",
                table: "GitContributionRuns");

            migrationBuilder.DropColumn(
                name: "NormalizedResultsJson",
                table: "GitContributionRuns");

            migrationBuilder.DropColumn(
                name: "PeerSnapshotId",
                table: "GitContributionRuns");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~",
                table: "GitContributionRuns",
                columns: new[] { "ProjectId", "GitEvidenceSnapshotId", "ScoringPolicyVersion" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitContributionRuns_Policy",
                table: "GitContributionRuns",
                sql: "\"ScoringPolicyVersion\" = 'ExperimentalGitScoringV1'");
        }
    }
}
