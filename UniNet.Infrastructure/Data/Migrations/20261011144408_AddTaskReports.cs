using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskReports", x => x.Id);
                    table.CheckConstraint("CK_TaskReports_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_TaskReports_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_TaskReports_ProjectTasks_ProjectTaskId",
                        column: x => x.ProjectTaskId,
                        principalTable: "ProjectTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskReports_Users_SubmittedById",
                        column: x => x.SubmittedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskEvidence_TaskReports_TaskReportId",
                        column: x => x.TaskReportId,
                        principalTable: "TaskReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskReportReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<short>(type: "smallint", nullable: false),
                    Feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskReportReviews", x => x.Id);
                    table.CheckConstraint("CK_TaskReportReviews_Decision", "\"Decision\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_TaskReportReviews_TaskReports_TaskReportId",
                        column: x => x.TaskReportId,
                        principalTable: "TaskReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskReportReviews_Users_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvidence_TaskReportId",
                table: "TaskEvidence",
                column: "TaskReportId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReportReviews_ReviewerId",
                table: "TaskReportReviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReportReviews_TaskReportId",
                table: "TaskReportReviews",
                column: "TaskReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_ProjectTaskId_SubmittedAt",
                table: "TaskReports",
                columns: new[] { "ProjectTaskId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_ProjectTaskId_SubmittedById_Version",
                table: "TaskReports",
                columns: new[] { "ProjectTaskId", "SubmittedById", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskReports_SubmittedById",
                table: "TaskReports",
                column: "SubmittedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskEvidence");

            migrationBuilder.DropTable(
                name: "TaskReportReviews");

            migrationBuilder.DropTable(
                name: "TaskReports");
        }
    }
}
