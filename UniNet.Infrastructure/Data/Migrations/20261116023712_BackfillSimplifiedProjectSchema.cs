using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSimplifiedProjectSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Retain original columns/tables: current consumers have not cut over yet.
            migrationBuilder.Sql("""
                INSERT INTO "ProjectDetails" ("ProjectId", "Description", "CompletedAt", "UpdatedAt")
                SELECT "Id", "Description", "CompletedAt", "UpdatedAt" FROM "Projects";

                DO $copy_dates$
                DECLARE p record; parsed date;
                BEGIN
                  FOR p IN SELECT "Id", "ImplementationStartDate", "ImplementationEndDate" FROM "Projects" LOOP
                    BEGIN
                      IF p."ImplementationStartDate" ~ '^\d{4}-\d{2}-\d{2}$' THEN
                        parsed := p."ImplementationStartDate"::date;
                        UPDATE "ProjectDetails" SET "StartDate" = parsed WHERE "ProjectId" = p."Id";
                      END IF;
                    EXCEPTION WHEN datetime_field_overflow OR invalid_datetime_format THEN NULL;
                    END;
                    BEGIN
                      IF p."ImplementationEndDate" ~ '^\d{4}-\d{2}-\d{2}$' THEN
                        parsed := p."ImplementationEndDate"::date;
                        UPDATE "ProjectDetails" SET "EndDate" = parsed WHERE "ProjectId" = p."Id";
                      END IF;
                    EXCEPTION WHEN datetime_field_overflow OR invalid_datetime_format THEN NULL;
                    END;
                  END LOOP;
                END $copy_dates$;

                UPDATE "Projects" p SET "Technologies" = s.names
                FROM (SELECT ps."ProjectId", jsonb_agg(sk."Name" ORDER BY sk."Name", sk."Id") AS names
                      FROM "ProjectSkills" ps JOIN "Skills" sk ON sk."Id" = ps."SkillId"
                      GROUP BY ps."ProjectId") s
                WHERE p."Id" = s."ProjectId" AND p."Technologies" IS NULL;

                INSERT INTO "ProjectRecruitments"
                  ("Id", "ProjectId", "Description", "Positions", "Deadline", "Status", "PublishedAt", "UpdatedAt")
                SELECT p."Id", p."Id", NULL,
                  jsonb_agg(jsonb_build_object('id', r."Id"::text, 'name', r."Role", 'quantity', r."Quantity",
                    'technologies', COALESCE(p."Technologies", '[]'::jsonb), 'skills', '[]'::jsonb,
                    'jobDescription', '', 'requirements', jsonb_build_array(r."Requirements"), 'benefits', '[]'::jsonb)
                    ORDER BY r."CreatedAt", r."Id"),
                  p."RecruitmentDeadline",
                  CASE WHEN p."Status" = 1 AND p."RecruitmentStatus" = 0
                    AND (p."RecruitmentDeadline" IS NULL OR p."RecruitmentDeadline" > CURRENT_TIMESTAMP)
                    THEN 1 ELSE 2 END,
                  NULL, p."UpdatedAt"
                FROM "Projects" p JOIN "ProjectRoleRequirements" r ON r."ProjectId" = p."Id"
                WHERE p."Origin" = 0
                GROUP BY p."Id";

                DO $check_ids$ BEGIN
                  IF EXISTS (SELECT 1 FROM "ProjectInvitations" i JOIN "ProjectJoinRequests" r ON r."Id" = i."Id") THEN
                    RAISE EXCEPTION 'Invitation/request ID collision: preserve original data and resolve before migration';
                  END IF;
                END $check_ids$;

                INSERT INTO "ProjectJoinRequests"
                  ("Id", "ProjectId", "UserId", "RequestType", "InviterId", "Role", "Message", "Status", "CreatedAt", "UpdatedAt")
                SELECT "Id", "ProjectId", "InviteeId", 1, "InviterId", "Role", "Message", "Status", "CreatedAt", "UpdatedAt"
                FROM "ProjectInvitations";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Backfill rollback requires a reviewed data export/reconciliation; original sources are preserved.");
        }
    }
}