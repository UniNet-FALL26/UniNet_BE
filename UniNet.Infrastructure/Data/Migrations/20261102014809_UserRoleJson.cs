using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserRoleJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "AssessorJson", table: "Users", type: "jsonb", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PartnerJson", table: "Users", type: "jsonb", nullable: true);
            migrationBuilder.AddColumn<string>(name: "StudentJson", table: "Users", type: "jsonb", nullable: true);
            // Copy every legacy value before removing columns, including data left after role changes.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "StudentJson" = jsonb_build_object(
                    'UniversityName', "UniversityName", 'Major', "Major", 'StudentCode', "StudentCode")
                WHERE "UniversityName" IS NOT NULL OR "Major" IS NOT NULL OR "StudentCode" IS NOT NULL;
                UPDATE "Users" SET "PartnerJson" = jsonb_build_object(
                    'OrganizationName', "OrganizationName", 'PartnerType', "PartnerType", 'Industry', "Industry", 'Website', "Website")
                WHERE "OrganizationName" IS NOT NULL OR "PartnerType" IS NOT NULL OR "Industry" IS NOT NULL OR "Website" IS NOT NULL;
                UPDATE "Users" AS u SET "AssessorJson" = jsonb_build_object(
                    'JobTitle', NULL, 'CompanyName', u."OrganizationName", 'YearsOfExperience', 0,
                    'Expertise', CASE WHEN NULLIF(btrim(u."Major"), '') IS NULL THEN '[]'::jsonb ELSE jsonb_build_array(u."Major") END,
                    'Education', NULL, 'University', u."UniversityName", 'LinkedInUrl', NULL, 'GitHubUrl', NULL, 'PortfolioUrl', NULL)
                FROM "Accounts" AS a WHERE a."Id" = u."AccountId" AND a."Role" = 2
                    AND (u."Major" IS NOT NULL OR u."UniversityName" IS NOT NULL OR u."OrganizationName" IS NOT NULL);
                """);
            migrationBuilder.DropIndex(
                name: "IX_Users_UniversityName_Major",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PartnerType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Major",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OrganizationName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PartnerType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StudentCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UniversityName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Users");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PartnerType",
                table: "Users",
                sql: "\"PartnerJson\" ->> 'PartnerType' IS NULL OR CAST(\"PartnerJson\" ->> 'PartnerType' AS INTEGER) BETWEEN 0 AND 4");
            migrationBuilder.Sql("""
                CREATE INDEX "IX_Users_UniversityName_Major" ON "Users"
                    (("StudentJson" ->> 'UniversityName'), ("StudentJson" ->> 'Major'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old schema has no destination for the new assessor-specific fields.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Users" WHERE "AssessorJson" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Export assessor profile JSON and clear it before downgrading UserRoleJson.';
                    END IF;
                END $$;
                DROP INDEX "IX_Users_UniversityName_Major";
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PartnerType",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Users",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Major",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationName",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "PartnerType",
                table: "Users",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudentCode",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UniversityName",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Users" SET
                    "UniversityName" = "StudentJson" ->> 'UniversityName', "Major" = "StudentJson" ->> 'Major',
                    "StudentCode" = "StudentJson" ->> 'StudentCode', "OrganizationName" = "PartnerJson" ->> 'OrganizationName',
                    "PartnerType" = ("PartnerJson" ->> 'PartnerType')::smallint,
                    "Industry" = "PartnerJson" ->> 'Industry', "Website" = "PartnerJson" ->> 'Website';
                """);
            migrationBuilder.DropColumn(name: "AssessorJson", table: "Users");
            migrationBuilder.DropColumn(name: "PartnerJson", table: "Users");
            migrationBuilder.DropColumn(name: "StudentJson", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UniversityName_Major",
                table: "Users",
                columns: new[] { "UniversityName", "Major" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PartnerType",
                table: "Users",
                sql: "\"PartnerType\" IS NULL OR \"PartnerType\" BETWEEN 0 AND 4");
        }
    }
}
