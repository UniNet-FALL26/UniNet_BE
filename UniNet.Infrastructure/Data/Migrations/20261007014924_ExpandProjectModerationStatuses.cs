using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandProjectModerationStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_ContentResult",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_LinkResult",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations");

            // Preserve existing decisions after Approved/Rejected changed from 1/2 to 3/4.
            migrationBuilder.Sql("""
                UPDATE "ProjectModerations"
                SET "Status" = CASE "Status" WHEN 1 THEN 3 WHEN 2 THEN 4 ELSE "Status" END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_ContentResult",
                table: "ProjectModerations",
                sql: "\"ContentResult\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_LinkResult",
                table: "ProjectModerations",
                sql: "\"LinkResult\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations",
                sql: "\"Status\" BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old schema cannot represent in-flight/review/error attempts without data loss.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ProjectModerations"
                        WHERE "Status" NOT IN (0, 3, 4) OR "ContentResult" = 3 OR "LinkResult" = 3) THEN
                        RAISE EXCEPTION 'Cannot downgrade while moderation attempts use new statuses or error results.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_ContentResult",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_LinkResult",
                table: "ProjectModerations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations");

            migrationBuilder.Sql("""
                UPDATE "ProjectModerations"
                SET "Status" = CASE "Status" WHEN 3 THEN 1 WHEN 4 THEN 2 ELSE "Status" END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_ContentResult",
                table: "ProjectModerations",
                sql: "\"ContentResult\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_LinkResult",
                table: "ProjectModerations",
                sql: "\"LinkResult\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectModerations_Status",
                table: "ProjectModerations",
                sql: "\"Status\" BETWEEN 0 AND 2");
        }
    }
}
