using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignDatabaseDesignV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Accounts_AccountId",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_GoogleId",
                table: "Accounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserProfiles",
                table: "UserProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserProfiles_PartnerType",
                table: "UserProfiles");

            migrationBuilder.RenameTable(
                name: "UserProfiles",
                newName: "Users");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "Users",
                newName: "FullName");

            migrationBuilder.RenameIndex(
                name: "IX_UserProfiles_AccountId",
                table: "Users",
                newName: "IX_Users_AccountId");

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationName",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "CareerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Headline = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CareerObjective = table.Column<string>(type: "text", nullable: true),
                    BasicInfoJson = table.Column<string>(type: "jsonb", nullable: true),
                    EducationJson = table.Column<string>(type: "jsonb", nullable: true),
                    SkillsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ExperienceJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProjectsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CertificatesJson = table.Column<string>(type: "jsonb", nullable: true),
                    ActivitiesJson = table.Column<string>(type: "jsonb", nullable: true),
                    LanguagesJson = table.Column<string>(type: "jsonb", nullable: true),
                    SocialLinksJson = table.Column<string>(type: "jsonb", nullable: true),
                    AppearanceJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IconUrl = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<short>(type: "smallint", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                    table.CheckConstraint("CK_Skills_Category", "\"Category\" BETWEEN 0 AND 8");
                });

            migrationBuilder.CreateTable(
                name: "UserVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationType = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    VerificationData = table.Column<string>(type: "jsonb", nullable: false),
                    DocumentUrls = table.Column<string>(type: "jsonb", nullable: true),
                    RejectReason = table.Column<string>(type: "text", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVerifications", x => x.Id);
                    table.CheckConstraint("CK_UserVerifications_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_UserVerifications_Type", "\"VerificationType\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_UserVerifications_Accounts_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserVerifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve the old name and legal data before removing the obsolete column.
            // Legacy verification has no review history: keep it pending for an actual review.
            migrationBuilder.Sql("""
                UPDATE "Users" u SET "Nickname" = left(u."FullName", 100),
                    "OrganizationName" = CASE WHEN a."Role" = 1 THEN u."FullName" ELSE NULL END
                FROM "Accounts" a WHERE a."Id" = u."AccountId";

                INSERT INTO "UserVerifications"
                    ("Id", "UserId", "VerificationType", "Status", "VerificationData", "SubmittedAt", "CreatedAt", "UpdatedAt")
                SELECT md5('uninet-v2-verification:' || u."Id"::text)::uuid, u."Id",
                    CASE WHEN a."Role" = 1 THEN 1 ELSE 0 END, 0,
                    jsonb_build_object('schemaVersion', 1, '_migration', 'database-v2',
                        'fullName', u."FullName", 'organizationName', u."OrganizationName",
                        'partnerType', u."PartnerType", 'taxCode', u."TaxCode",
                        'universityName', u."UniversityName", 'major', u."Major", 'studentCode', u."StudentCode",
                        'industry', u."Industry", 'website', u."Website", 'contactEmail', u."ContactEmail",
                        'phone', u."Phone", 'address', u."Address", 'legacyIsVerified', u."IsVerified"),
                    u."CreatedAt", u."CreatedAt", u."UpdatedAt"
                FROM "Users" u JOIN "Accounts" a ON a."Id" = u."AccountId"
                WHERE u."TaxCode" IS NOT NULL OR u."IsVerified";

                UPDATE "Users" SET "IsVerified" = FALSE WHERE "IsVerified";
                """);
            migrationBuilder.DropColumn(name: "TaxCode", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GoogleId",
                table: "Accounts",
                column: "GoogleId",
                unique: true,
                filter: "\"GoogleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UniversityName_Major",
                table: "Users",
                columns: new[] { "UniversityName", "Major" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PartnerType",
                table: "Users",
                sql: "\"PartnerType\" IS NULL OR \"PartnerType\" BETWEEN 0 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_CareerProfiles_ProjectsJson",
                table: "CareerProfiles",
                column: "ProjectsJson")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_CareerProfiles_SkillsJson",
                table: "CareerProfiles",
                column: "SkillsJson")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_CareerProfiles_UserId",
                table: "CareerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Category_IsActive_DisplayOrder",
                table: "Skills",
                columns: new[] { "Category", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Slug",
                table: "Skills",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserVerifications_ReviewedBy",
                table: "UserVerifications",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserVerifications_UserId_Status",
                table: "UserVerifications",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Accounts_AccountId",
                table: "Users",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to silently discard data created after this migration.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "CareerProfiles") OR EXISTS (SELECT 1 FROM "Skills")
                        OR EXISTS (SELECT 1 FROM "UserVerifications"
                            WHERE "VerificationData"->>'_migration' IS DISTINCT FROM 'database-v2' OR "Status" <> 0)
                        OR EXISTS (SELECT 1 FROM "Users" u JOIN "Accounts" a ON a."Id" = u."AccountId"
                            WHERE u."Nickname" IS DISTINCT FROM left(u."FullName", 100)
                                OR (a."Role" = 1 AND u."FullName" IS DISTINCT FROM u."OrganizationName"))
                        OR EXISTS (SELECT 1 FROM "Users" u JOIN "UserVerifications" v ON v."UserId" = u."Id"
                            WHERE u."FullName" IS DISTINCT FROM v."VerificationData"->>'fullName'
                                OR u."OrganizationName" IS DISTINCT FROM v."VerificationData"->>'organizationName')
                    THEN RAISE EXCEPTION 'Database v2 contains new business data; export it before rollback.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<string>(name: "TaxCode", table: "Users", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Users" u SET "TaxCode" = v."VerificationData"->>'taxCode',
                    "IsVerified" = COALESCE((v."VerificationData"->>'legacyIsVerified')::boolean, FALSE)
                FROM "UserVerifications" v WHERE v."UserId" = u."Id"
                    AND v."VerificationData"->>'_migration' = 'database-v2';
                UPDATE "Users" u SET "FullName" = u."OrganizationName"
                FROM "Accounts" a WHERE a."Id" = u."AccountId" AND a."Role" = 1 AND u."OrganizationName" IS NOT NULL;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Accounts_AccountId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "CareerProfiles");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "UserVerifications");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_GoogleId",
                table: "Accounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_UniversityName_Major",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PartnerType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OrganizationName",
                table: "Users");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "UserProfiles");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "UserProfiles",
                newName: "DisplayName");

            migrationBuilder.RenameIndex(
                name: "IX_Users_AccountId",
                table: "UserProfiles",
                newName: "IX_UserProfiles_AccountId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserProfiles",
                table: "UserProfiles",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GoogleId",
                table: "Accounts",
                column: "GoogleId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserProfiles_PartnerType",
                table: "UserProfiles",
                sql: "\"PartnerType\" IS NULL OR \"PartnerType\" BETWEEN 0 AND 4");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Accounts_AccountId",
                table: "UserProfiles",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
