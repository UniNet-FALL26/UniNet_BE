using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialContributionV2Workflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitContributionRuns_Id_ProjectId",
                table: "GitContributionRuns",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "ProjectContributionWorkflows",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SelectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectContributionWorkflows", x => x.ProjectId);
                    table.CheckConstraint("CK_OfficialWorkflow_Mode", "\"Mode\" IN ('OfficialV2','LegacyV1','RequiresOptIn')");
                    table.ForeignKey(
                        name: "FK_ProjectContributionWorkflows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficialContributionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ResultHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinalizedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialContributionRuns", x => x.Id);
                    table.UniqueConstraint("AK_OfficialContributionRuns_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_OfficialRun_Finalized", "(\"State\" = 'Finalized') = (\"FinalizedAt\" IS NOT NULL AND \"FinalizedById\" IS NOT NULL)");
                    table.CheckConstraint("CK_OfficialRun_Revision", "\"Revision\" >= 0 AND length(\"ResultHash\") = 64");
                    table.CheckConstraint("CK_OfficialRun_State", "\"State\" IN ('Draft','Review','Approved','Rejected','Finalized')");
                    table.ForeignKey(
                        name: "FK_OfficialContributionRuns_GitContributionRuns_Id_ProjectId",
                        columns: x => new { x.Id, x.ProjectId },
                        principalTable: "GitContributionRuns",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialContributionRuns_ProjectContributionWorkflows_Proje~",
                        column: x => x.ProjectId,
                        principalTable: "ProjectContributionWorkflows",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialContributionRuns_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialContributionRuns_Users_FinalizedById",
                        column: x => x.FinalizedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialContributionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialContributionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficialContributionAudits_OfficialContributionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OfficialContributionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialContributionAudits_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialContributionPublications",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedById = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialContributionPublications", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_OfficialContributionPublications_OfficialContributionRuns_R~",
                        columns: x => new { x.RunId, x.ProjectId },
                        principalTable: "OfficialContributionRuns",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialContributionPublications_Users_PublishedById",
                        column: x => x.PublishedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionAudits_ActorId",
                table: "OfficialContributionAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionAudits_RunId_Revision",
                table: "OfficialContributionAudits",
                columns: new[] { "RunId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionPublications_PublishedById",
                table: "OfficialContributionPublications",
                column: "PublishedById");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionPublications_RunId",
                table: "OfficialContributionPublications",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionPublications_RunId_ProjectId",
                table: "OfficialContributionPublications",
                columns: new[] { "RunId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionRuns_CreatedById",
                table: "OfficialContributionRuns",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionRuns_FinalizedById",
                table: "OfficialContributionRuns",
                column: "FinalizedById");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionRuns_ProjectId",
                table: "OfficialContributionRuns",
                column: "ProjectId",
                unique: true,
                filter: "\"State\" = 'Finalized'");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionRuns_ProjectId_RequestId",
                table: "OfficialContributionRuns",
                columns: new[] { "ProjectId", "RequestId" },
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "ProjectContributionWorkflows" ("ProjectId", "Mode", "SelectedAt")
                SELECT p."Id", CASE WHEN EXISTS(SELECT 1 FROM "ContributionCycles" c WHERE c."ProjectId"=p."Id" AND c."Status"=2)
                  THEN 'LegacyV1' ELSE 'RequiresOptIn' END, CURRENT_TIMESTAMP FROM "Projects" p;
                CREATE FUNCTION uninet_official_default_workflow() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  INSERT INTO "ProjectContributionWorkflows" ("ProjectId","Mode","SelectedAt") VALUES(NEW."Id",'OfficialV2',CURRENT_TIMESTAMP);
                  RETURN NEW;
                END $$;
                CREATE TRIGGER official_project_workflow AFTER INSERT ON "Projects" FOR EACH ROW EXECUTE FUNCTION uninet_official_default_workflow();
                CREATE FUNCTION uninet_official_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'official audit data is immutable' USING ERRCODE='55000'; END $$;
                CREATE FUNCTION uninet_official_workflow_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' AND NOT EXISTS(SELECT 1 FROM "Projects" WHERE "Id"=OLD."ProjectId")
                    AND NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "ProjectId"=OLD."ProjectId") THEN RETURN OLD; END IF;
                  RAISE EXCEPTION 'workflow selection is immutable' USING ERRCODE='55000';
                END $$;
                CREATE TRIGGER official_workflow_immutable BEFORE UPDATE OR DELETE ON "ProjectContributionWorkflows" FOR EACH ROW EXECUTE FUNCTION uninet_official_workflow_guard();
                CREATE TRIGGER official_audit_immutable BEFORE UPDATE OR DELETE ON "OfficialContributionAudits" FOR EACH ROW EXECUTE FUNCTION uninet_official_append_only();
                CREATE TRIGGER official_publication_immutable BEFORE UPDATE OR DELETE ON "OfficialContributionPublications" FOR EACH ROW EXECUTE FUNCTION uninet_official_append_only();
                CREATE FUNCTION uninet_official_calculation_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "Id"=OLD."Id") THEN
                    RAISE EXCEPTION 'official calculation is frozen' USING ERRCODE='55000';
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
                END $$;
                CREATE TRIGGER official_calculation_immutable BEFORE UPDATE OR DELETE ON "GitContributionRuns" FOR EACH ROW EXECUTE FUNCTION uninet_official_calculation_guard();
                CREATE FUNCTION uninet_official_run_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'official runs retain audit history' USING ERRCODE='55000'; END IF;
                  IF TG_OP='INSERT' THEN
                    PERFORM 1 FROM "GitContributionRuns" WHERE "Id"=NEW."Id" AND "ProjectId"=NEW."ProjectId" FOR UPDATE;
                    IF NEW."State"<>'Draft' OR NEW."Revision"<>0 OR
                      NOT EXISTS(SELECT 1 FROM "ProjectContributionWorkflows" WHERE "ProjectId"=NEW."ProjectId" AND "Mode"='OfficialV2') OR
                      NOT EXISTS(SELECT 1 FROM "GitContributionRuns" WHERE "Id"=NEW."Id" AND "ProjectId"=NEW."ProjectId"
                        AND "ScoringPolicyVersion"='ExperimentalGitScoringV2' AND "CalculationStatus"='Ready'
                        AND "PeerSnapshotId" IS NOT NULL AND "NormalizationPolicyVersion"='GitPeerSeparate60_40V1') THEN
                      RAISE EXCEPTION 'official ready V2 calculation required' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF OLD."State"='Finalized' OR (NEW."Id",NEW."ProjectId",NEW."RequestId",NEW."ResultHash",NEW."CreatedAt",NEW."CreatedById")
                     IS DISTINCT FROM (OLD."Id",OLD."ProjectId",OLD."RequestId",OLD."ResultHash",OLD."CreatedAt",OLD."CreatedById") THEN
                    RAISE EXCEPTION 'official frozen result cannot change' USING ERRCODE='55000';
                  END IF;
                  IF NEW."Revision"<>OLD."Revision"+1 OR NOT (
                    (OLD."State" IN ('Draft','Rejected') AND NEW."State"='Review') OR (OLD."State"='Review' AND NEW."State" IN ('Approved','Rejected')) OR
                    (OLD."State"='Approved' AND NEW."State"='Finalized')) THEN
                    RAISE EXCEPTION 'invalid official lifecycle transition' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER official_run_integrity BEFORE INSERT OR UPDATE OR DELETE ON "OfficialContributionRuns" FOR EACH ROW EXECUTE FUNCTION uninet_official_run_guard();
                CREATE FUNCTION uninet_official_publication_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  PERFORM 1 FROM "OfficialContributionRuns" WHERE "Id"=NEW."RunId" AND "ProjectId"=NEW."ProjectId" FOR UPDATE;
                  IF NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "Id"=NEW."RunId" AND "ProjectId"=NEW."ProjectId" AND "State"='Finalized') THEN
                    RAISE EXCEPTION 'only finalized official results can be published' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER official_publish_integrity BEFORE INSERT ON "OfficialContributionPublications" FOR EACH ROW EXECUTE FUNCTION uninet_official_publication_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "OfficialContributionRuns") OR EXISTS(SELECT 1 FROM "ProjectContributionWorkflows" WHERE "Mode"='OfficialV2') THEN
                    RAISE EXCEPTION 'retain official workflow and result audit before rollback' USING ERRCODE='55000';
                  END IF;
                END $$;
                DROP TRIGGER official_project_workflow ON "Projects";
                DROP TRIGGER official_calculation_immutable ON "GitContributionRuns";
                """);
            migrationBuilder.DropTable(
                name: "OfficialContributionAudits");

            migrationBuilder.DropTable(
                name: "OfficialContributionPublications");

            migrationBuilder.DropTable(
                name: "OfficialContributionRuns");

            migrationBuilder.DropTable(
                name: "ProjectContributionWorkflows");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitContributionRuns_Id_ProjectId",
                table: "GitContributionRuns");
            migrationBuilder.Sql("""
                DROP FUNCTION uninet_official_default_workflow();
                DROP FUNCTION uninet_official_append_only();
                DROP FUNCTION uninet_official_workflow_guard();
                DROP FUNCTION uninet_official_calculation_guard();
                DROP FUNCTION uninet_official_run_guard();
                DROP FUNCTION uninet_official_publication_guard();
                """);
        }
    }
}
