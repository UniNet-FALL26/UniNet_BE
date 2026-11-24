using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace UniNet.Infrastructure.Data.Migrations;
public partial class AddOfficialContributionPublicationVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
                name: "IX_OfficialContributionRuns_ProjectId",
                table: "OfficialContributionRuns");
        migrationBuilder.Sql("""
            CREATE TABLE "OfficialContributionReleases" (
              "RunId" uuid NOT NULL CONSTRAINT "PK_OfficialContributionReleases" PRIMARY KEY,
              "ProjectId" uuid NOT NULL,
              "Version" integer NOT NULL,
              "PreviousRunId" uuid NULL,
              "PublishedById" uuid NOT NULL CONSTRAINT "FK_OfficialContributionReleases_Users_PublishedById" REFERENCES "Users"("Id") ON DELETE RESTRICT,
              "PublishedAt" timestamp with time zone NOT NULL,
              "Reason" varchar(2000) NULL,
              CONSTRAINT "CK_OfficialRelease_Version" CHECK ("Version">0),
              CONSTRAINT "FK_OfficialContributionReleases_OfficialContributionRuns_RunId~" FOREIGN KEY ("RunId","ProjectId") REFERENCES "OfficialContributionRuns"("Id","ProjectId") ON DELETE RESTRICT,
              CONSTRAINT "FK_OfficialContributionReleases_OfficialContributionRuns_Previ~" FOREIGN KEY ("PreviousRunId","ProjectId") REFERENCES "OfficialContributionRuns"("Id","ProjectId") ON DELETE RESTRICT
            );
            """);
        migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionRuns_ProjectId",
                table: "OfficialContributionRuns",
                column: "ProjectId");
        migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionReleases_PreviousRunId_ProjectId",
                table: "OfficialContributionReleases",
                columns: new[] { "PreviousRunId", "ProjectId" });
        migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionReleases_ProjectId_Version",
                table: "OfficialContributionReleases",
                columns: new[] { "ProjectId", "Version" },
                unique: true);
        migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionReleases_PublishedById",
                table: "OfficialContributionReleases",
                column: "PublishedById");
        migrationBuilder.CreateIndex(
                name: "IX_OfficialContributionReleases_RunId_ProjectId",
                table: "OfficialContributionReleases",
                columns: new[] { "RunId", "ProjectId" },
                unique: true);        migrationBuilder.Sql("""
            -- Import the immutable first-publication receipts without modifying source rows.
            INSERT INTO "OfficialContributionReleases" ("RunId","ProjectId","Version","PreviousRunId","PublishedById","PublishedAt","Reason")
            SELECT "RunId","ProjectId",1,NULL,"PublishedById","PublishedAt",NULL FROM "OfficialContributionPublications";
            CREATE FUNCTION uninet_official_release_guard() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE prior "OfficialContributionReleases"%ROWTYPE;
            BEGIN
              IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'publication history is immutable' USING ERRCODE='55000'; END IF;
              PERFORM 1 FROM "Projects" WHERE "Id"=NEW."ProjectId" FOR UPDATE;
              SELECT * INTO prior FROM "OfficialContributionReleases" WHERE "ProjectId"=NEW."ProjectId" ORDER BY "Version" DESC LIMIT 1;
              IF NEW."Version"<>COALESCE(prior."Version",0)+1 OR NEW."PreviousRunId" IS DISTINCT FROM prior."RunId" THEN
                RAISE EXCEPTION 'publication version changed' USING ERRCODE='23514';
              END IF;
              IF NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "Id"=NEW."RunId" AND "ProjectId"=NEW."ProjectId" AND "State"='Finalized') THEN
                RAISE EXCEPTION 'finalized project result required' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER official_release_integrity BEFORE INSERT OR UPDATE OR DELETE ON "OfficialContributionReleases"
              FOR EACH ROW EXECUTE FUNCTION uninet_official_release_guard();
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS(SELECT 1 FROM "OfficialContributionReleases") THEN
                RAISE EXCEPTION 'retain publication history; destructive rollback forbidden' USING ERRCODE='55000';
              END IF;
            END $$;
            DROP TABLE "OfficialContributionReleases";
            DROP FUNCTION uninet_official_release_guard();
            """);
        migrationBuilder.DropIndex(name:"IX_OfficialContributionRuns_ProjectId",table:"OfficialContributionRuns");
        migrationBuilder.CreateIndex(name:"IX_OfficialContributionRuns_ProjectId",table:"OfficialContributionRuns",column:"ProjectId",unique:true,filter:"\"State\" = 'Finalized'");
    }
}