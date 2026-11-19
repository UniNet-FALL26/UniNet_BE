using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAndRemoveLegacyProjectTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL TIME ZONE 'UTC';
                DO $archive$
                DECLARE
                  source_schema text := current_schema();
                  archive_schema text := 'project_legacy_archive_' || md5(current_schema());
                  source_table text;
                  payload jsonb;
                  names text[] := ARRAY['ProjectRoleRequirements','ProjectSkills','ProjectInvitations',
                    'WorkspaceChannels','WorkspaceMessages','ProjectTasks','ProjectTaskAssignments',
                    'TaskReports','TaskEvidence','TaskReportReviews','ExternalProjectDrafts'];
                BEGIN
                  -- Lock sources until this migration transaction commits, preventing late writes.
                  FOREACH source_table IN ARRAY names LOOP
                    EXECUTE format('LOCK TABLE %I.%I IN ACCESS EXCLUSIVE MODE', source_schema, source_table);
                  END LOOP;
                  EXECUTE format('CREATE SCHEMA %I', archive_schema);
                  EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC', archive_schema);
                  EXECUTE format('CREATE TABLE %I."Snapshots" (
                    "TableName" text PRIMARY KEY, "SourceSchema" text NOT NULL,
                    "RowCount" bigint NOT NULL, "Data" jsonb NOT NULL, "Checksum" text NOT NULL,
                    CHECK (jsonb_array_length("Data") = "RowCount" AND md5("Data"::text) = "Checksum"))', archive_schema);
                  FOREACH source_table IN ARRAY names LOOP
                    EXECUTE format('SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text COLLATE "C"), ''[]''::jsonb)
                      FROM %I.%I t', source_schema, source_table) INTO payload;
                    EXECUTE format('INSERT INTO %I."Snapshots" VALUES ($1,$2,$3,$4,$5)', archive_schema)
                      USING source_table, source_schema, jsonb_array_length(payload), payload, md5(payload::text);
                  END LOOP;
                  EXECUTE format('CREATE FUNCTION %I.reject_write() RETURNS trigger LANGUAGE plpgsql AS
                    $fn$ BEGIN RAISE EXCEPTION ''Project legacy archive is read-only''; END $fn$', archive_schema);
                  EXECUTE format('CREATE TRIGGER readonly_archive BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
                    ON %I."Snapshots" FOR EACH STATEMENT EXECUTE FUNCTION %I.reject_write()', archive_schema, archive_schema);
                END $archive$;

                -- Preserve catalog identity/display metadata as embedded technology values.
                UPDATE "Projects" p SET "Technologies" = (
                  SELECT COALESCE(jsonb_agg(value), '[]'::jsonb) FROM (
                    SELECT element AS value FROM jsonb_array_elements(COALESCE(p."Technologies", '[]'::jsonb)) element
                    WHERE NOT EXISTS (
                      SELECT 1 FROM "ProjectSkills" ps JOIN "Skills" s ON s."Id" = ps."SkillId"
                      WHERE ps."ProjectId" = p."Id" AND
                        (element ->> 'skillId' = s."Id"::text OR element ->> 'name' = s."Name"
                          OR (jsonb_typeof(element) = 'string' AND element #>> '{}' = s."Name")))
                    UNION ALL
                    SELECT jsonb_build_object('skillId',s."Id",'name',s."Name",'category',s."Category",'iconUrl',s."IconUrl")
                    FROM "ProjectSkills" ps JOIN "Skills" s ON s."Id" = ps."SkillId" WHERE ps."ProjectId" = p."Id"
                  ) technologies
                ) WHERE EXISTS (SELECT 1 FROM "ProjectSkills" ps WHERE ps."ProjectId" = p."Id");

                INSERT INTO "ProjectRecruitments"
                  ("Id","ProjectId","Description","Positions","Deadline","Status","PublishedAt","UpdatedAt")
                SELECT p."Id",p."Id",NULL,'[]'::jsonb,p."RecruitmentDeadline",2,NULL,p."UpdatedAt"
                FROM "Projects" p WHERE p."Origin" = 0 AND EXISTS
                  (SELECT 1 FROM "ProjectRoleRequirements" r WHERE r."ProjectId" = p."Id")
                ON CONFLICT ("ProjectId") DO NOTHING;

                UPDATE "ProjectRecruitments" recruitment SET "Positions" = (
                  SELECT COALESCE(jsonb_agg(value), '[]'::jsonb) FROM (
                    SELECT element AS value FROM jsonb_array_elements(recruitment."Positions") element
                    WHERE NOT EXISTS (SELECT 1 FROM "ProjectRoleRequirements" r
                      WHERE r."ProjectId" = recruitment."ProjectId" AND element ->> 'id' = r."Id"::text)
                    UNION ALL
                    SELECT jsonb_build_object('technologies','[]'::jsonb,'skills','[]'::jsonb,'jobDescription','', 'benefits','[]'::jsonb)
                      || COALESCE((SELECT item FROM jsonb_array_elements(recruitment."Positions") item
                        WHERE item ->> 'id' = r."Id"::text LIMIT 1), '{}'::jsonb)
                      || jsonb_build_object('id',r."Id",'name',r."Role",'quantity',r."Quantity",
                        'requirements',jsonb_build_array(r."Requirements"))
                    FROM "ProjectRoleRequirements" r WHERE r."ProjectId" = recruitment."ProjectId"
                  ) positions
                ) WHERE EXISTS (SELECT 1 FROM "ProjectRoleRequirements" r WHERE r."ProjectId" = recruitment."ProjectId");

                DO $collisions$ BEGIN
                  IF EXISTS (SELECT 1 FROM "ProjectInvitations" i JOIN "ProjectJoinRequests" r ON r."Id" = i."Id"
                    WHERE r."RequestType" <> 1 OR r."ProjectId" <> i."ProjectId" OR r."UserId" <> i."InviteeId"
                      OR r."InviterId" IS DISTINCT FROM i."InviterId") THEN
                    RAISE EXCEPTION 'Invitation/request identity collision; migration aborted without dropping data';
                  END IF;
                END $collisions$;
                INSERT INTO "ProjectJoinRequests" AS target
                  ("Id","ProjectId","UserId","RequestType","InviterId","Role","Message","Status","CreatedAt","UpdatedAt")
                SELECT "Id","ProjectId","InviteeId",1,"InviterId","Role","Message","Status","CreatedAt","UpdatedAt"
                FROM "ProjectInvitations"
                ON CONFLICT ("Id") DO UPDATE SET "Role" = EXCLUDED."Role", "Message" = EXCLUDED."Message",
                  "Status" = EXCLUDED."Status", "UpdatedAt" = EXCLUDED."UpdatedAt"
                WHERE EXCLUDED."UpdatedAt" > target."UpdatedAt";
                """);

            migrationBuilder.DropTable(
                name: "ExternalProjectDrafts");

            migrationBuilder.DropTable(
                name: "ProjectInvitations");

            migrationBuilder.DropTable(
                name: "ProjectRoleRequirements");

            migrationBuilder.DropTable(
                name: "ProjectSkills");

            migrationBuilder.DropTable(
                name: "ProjectTaskAssignments");

            migrationBuilder.DropTable(
                name: "TaskEvidence");

            migrationBuilder.DropTable(
                name: "TaskReportReviews");

            migrationBuilder.DropTable(
                name: "WorkspaceMessages");

            migrationBuilder.DropTable(
                name: "TaskReports");

            migrationBuilder.DropTable(
                name: "WorkspaceChannels");

            migrationBuilder.DropTable(
                name: "ProjectTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore requires a reviewed reconstruction from the read-only project legacy archive; automatic rollback would lose replacement-schema writes.");
        }
    }
}
