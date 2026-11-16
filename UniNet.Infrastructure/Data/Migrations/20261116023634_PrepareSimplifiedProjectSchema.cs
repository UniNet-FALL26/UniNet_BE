using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrepareSimplifiedProjectSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectJoinRequests_Status",
                table: "ProjectJoinRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "ChatRoomId",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Technologies",
                table: "Projects",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JoinRequestId",
                table: "ProjectMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PositionId",
                table: "ProjectMembers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InviterId",
                table: "ProjectJoinRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PositionId",
                table: "ProjectJoinRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "RequestType",
                table: "ProjectJoinRequests",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "ProjectJoinRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "ProjectJoinRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectDetails",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Features = table.Column<string>(type: "jsonb", nullable: true),
                    Results = table.Column<string>(type: "jsonb", nullable: true),
                    Documents = table.Column<string>(type: "jsonb", nullable: true),
                    Media = table.Column<string>(type: "jsonb", nullable: true),
                    Links = table.Column<string>(type: "jsonb", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDetails", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_ProjectDetails_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPersonalReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Challenges = table.Column<string>(type: "text", nullable: true),
                    LessonsLearned = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPersonalReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPersonalReviews_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectPersonalReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectRecruitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Positions = table.Column<string>(type: "jsonb", nullable: false),
                    Deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRecruitments", x => x.Id);
                    table.CheckConstraint("CK_ProjectRecruitments_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ProjectRecruitments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_JoinRequestId",
                table: "ProjectMembers",
                column: "JoinRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectJoinRequests_InviterId",
                table: "ProjectJoinRequests",
                column: "InviterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectJoinRequests_ReviewedBy",
                table: "ProjectJoinRequests",
                column: "ReviewedBy");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectJoinRequests_RequestType",
                table: "ProjectJoinRequests",
                sql: "\"RequestType\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectJoinRequests_Status",
                table: "ProjectJoinRequests",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPersonalReviews_ProjectId_UserId",
                table: "ProjectPersonalReviews",
                columns: new[] { "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPersonalReviews_UserId",
                table: "ProjectPersonalReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRecruitments_ProjectId",
                table: "ProjectRecruitments",
                column: "ProjectId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectJoinRequests_Users_InviterId",
                table: "ProjectJoinRequests",
                column: "InviterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectJoinRequests_Users_ReviewedBy",
                table: "ProjectJoinRequests",
                column: "ReviewedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectMembers_ProjectJoinRequests_JoinRequestId",
                table: "ProjectMembers",
                column: "JoinRequestId",
                principalTable: "ProjectJoinRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM "ProjectDetails")
                    OR EXISTS (SELECT 1 FROM "ProjectRecruitments")
                    OR EXISTS (SELECT 1 FROM "ProjectPersonalReviews")
                    OR EXISTS (SELECT 1 FROM "Projects" WHERE "LogoUrl" IS NOT NULL OR "Technologies" IS NOT NULL OR "ChatRoomId" IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM "ProjectJoinRequests" WHERE "Status" = 4 OR "RequestType" <> 0
                      OR "InviterId" IS NOT NULL OR "PositionId" IS NOT NULL OR "ReviewedBy" IS NOT NULL OR "ReviewedAt" IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM "ProjectMembers" WHERE "JoinRequestId" IS NOT NULL OR "PositionId" IS NOT NULL)
                  THEN RAISE EXCEPTION 'Project schema rollback would lose data; export/reconcile before rollback'; END IF;
                END $guard$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectJoinRequests_Users_InviterId",
                table: "ProjectJoinRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectJoinRequests_Users_ReviewedBy",
                table: "ProjectJoinRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectMembers_ProjectJoinRequests_JoinRequestId",
                table: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "ProjectDetails");

            migrationBuilder.DropTable(
                name: "ProjectPersonalReviews");

            migrationBuilder.DropTable(
                name: "ProjectRecruitments");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMembers_JoinRequestId",
                table: "ProjectMembers");

            migrationBuilder.DropIndex(
                name: "IX_ProjectJoinRequests_InviterId",
                table: "ProjectJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProjectJoinRequests_ReviewedBy",
                table: "ProjectJoinRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectJoinRequests_RequestType",
                table: "ProjectJoinRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectJoinRequests_Status",
                table: "ProjectJoinRequests");

            migrationBuilder.DropColumn(
                name: "ChatRoomId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Technologies",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "JoinRequestId",
                table: "ProjectMembers");

            migrationBuilder.DropColumn(
                name: "PositionId",
                table: "ProjectMembers");

            migrationBuilder.DropColumn(
                name: "InviterId",
                table: "ProjectJoinRequests");

            migrationBuilder.DropColumn(
                name: "PositionId",
                table: "ProjectJoinRequests");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "ProjectJoinRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "ProjectJoinRequests");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "ProjectJoinRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectJoinRequests_Status",
                table: "ProjectJoinRequests",
                sql: "\"Status\" BETWEEN 0 AND 3");
        }
    }
}
