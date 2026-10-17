using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContributionModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContributionCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    OpenedById = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FormulaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionCycles", x => x.Id);
                    table.CheckConstraint("CK_ContributionCycles_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ContributionCycles_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionCycles_Users_OpenedById",
                        column: x => x.OpenedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionCalculationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunNumber = table.Column<int>(type: "integer", nullable: false),
                    FormulaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InputSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CalculatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionCalculationRuns", x => x.Id);
                    table.UniqueConstraint("AK_ContributionCalculationRuns_Id_CycleId", x => new { x.Id, x.CycleId });
                    table.CheckConstraint("CK_ContributionCalculationRuns_RunNumber", "\"RunNumber\" > 0");
                    table.CheckConstraint("CK_ContributionCalculationRuns_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ContributionCalculationRuns_ContributionCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "ContributionCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionCalculationRuns_Users_CalculatedById",
                        column: x => x.CalculatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleAtSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    MembershipStatusAtSnapshot = table.Column<short>(type: "smallint", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsLeader = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionParticipants", x => x.Id);
                    table.UniqueConstraint("AK_ContributionParticipants_CycleId_UserId", x => new { x.CycleId, x.UserId });
                    table.CheckConstraint("CK_ContributionParticipants_MembershipStatus", "\"MembershipStatusAtSnapshot\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ContributionParticipants_ContributionCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "ContributionCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<short>(type: "smallint", nullable: false),
                    Feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionReviews", x => x.Id);
                    table.CheckConstraint("CK_ContributionReviews_Decision", "\"Decision\" BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_ContributionReviews_RevisionFeedback", "\"Decision\" <> 1 OR length(trim(\"Feedback\")) > 0");
                    table.ForeignKey(
                        name: "FK_ContributionReviews_ContributionCalculationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ContributionCalculationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionReviews_Users_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionPeerEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluateeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionPeerEvaluations", x => x.Id);
                    table.CheckConstraint("CK_ContributionPeerEvaluations_NoSelf", "\"EvaluatorUserId\" <> \"EvaluateeUserId\"");
                    table.CheckConstraint("CK_ContributionPeerEvaluations_Score", "\"Score\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluations_ContributionCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "ContributionCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluations_ContributionParticipants_CycleI~",
                        columns: x => new { x.CycleId, x.EvaluateeUserId },
                        principalTable: "ContributionParticipants",
                        principalColumns: new[] { "CycleId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluations_ContributionParticipants_Cycle~1",
                        columns: x => new { x.CycleId, x.EvaluatorUserId },
                        principalTable: "ContributionParticipants",
                        principalColumns: new[] { "CycleId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluations_Users_EvaluateeUserId",
                        column: x => x.EvaluateeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluations_Users_EvaluatorUserId",
                        column: x => x.EvaluatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskExecutionScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ApprovedDeliverablesScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PeerEvaluationScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CalculationDetails = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionResults", x => x.Id);
                    table.CheckConstraint("CK_ContributionResults_Scores", "CAST(\"TaskExecutionScore\" AS NUMERIC) BETWEEN 0 AND 100 AND CAST(\"ApprovedDeliverablesScore\" AS NUMERIC) BETWEEN 0 AND 100 AND CAST(\"PeerEvaluationScore\" AS NUMERIC) BETWEEN 0 AND 100 AND CAST(\"TotalScore\" AS NUMERIC) BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ContributionResults_ContributionCalculationRuns_RunId_Cycle~",
                        columns: x => new { x.RunId, x.CycleId },
                        principalTable: "ContributionCalculationRuns",
                        principalColumns: new[] { "Id", "CycleId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionResults_ContributionParticipants_CycleId_UserId",
                        columns: x => new { x.CycleId, x.UserId },
                        principalTable: "ContributionParticipants",
                        principalColumns: new[] { "CycleId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionResults_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContributionPeerEvaluationHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluateeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionPeerEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousScore = table.Column<int>(type: "integer", nullable: true),
                    PreviousComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewScore = table.Column<int>(type: "integer", nullable: false),
                    NewComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionPeerEvaluationHistory", x => x.Id);
                    table.CheckConstraint("CK_ContributionPeerEvaluationHistory_Action", "\"Action\" BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_ContributionPeerEvaluationHistory_NewScore", "\"NewScore\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ContributionPeerEvaluationHistory_PreviousScore", "\"PreviousScore\" IS NULL OR \"PreviousScore\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluationHistory_ContributionCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "ContributionCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluationHistory_ContributionParticipants_~",
                        columns: x => new { x.CycleId, x.EvaluateeUserId },
                        principalTable: "ContributionParticipants",
                        principalColumns: new[] { "CycleId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluationHistory_ContributionParticipants~1",
                        columns: x => new { x.CycleId, x.EvaluatorUserId },
                        principalTable: "ContributionParticipants",
                        principalColumns: new[] { "CycleId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluationHistory_ContributionPeerEvaluatio~",
                        column: x => x.ContributionPeerEvaluationId,
                        principalTable: "ContributionPeerEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionPeerEvaluationHistory_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionCalculationRuns_CalculatedById",
                table: "ContributionCalculationRuns",
                column: "CalculatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionCalculationRuns_CycleId_RunNumber",
                table: "ContributionCalculationRuns",
                columns: new[] { "CycleId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionCalculationRuns_CycleId_Status",
                table: "ContributionCalculationRuns",
                columns: new[] { "CycleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionCycles_OpenedById",
                table: "ContributionCycles",
                column: "OpenedById");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionCycles_ProjectId",
                table: "ContributionCycles",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionParticipants_CycleId_UserId",
                table: "ContributionParticipants",
                columns: new[] { "CycleId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionParticipants_UserId",
                table: "ContributionParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluationHistory_ActorUserId",
                table: "ContributionPeerEvaluationHistory",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluationHistory_ContributionPeerEvaluatio~",
                table: "ContributionPeerEvaluationHistory",
                column: "ContributionPeerEvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluationHistory_CycleId_EvaluateeUserId",
                table: "ContributionPeerEvaluationHistory",
                columns: new[] { "CycleId", "EvaluateeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluationHistory_CycleId_EvaluatorUserId_E~",
                table: "ContributionPeerEvaluationHistory",
                columns: new[] { "CycleId", "EvaluatorUserId", "EvaluateeUserId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluations_CycleId_EvaluateeUserId",
                table: "ContributionPeerEvaluations",
                columns: new[] { "CycleId", "EvaluateeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluations_CycleId_EvaluatorUserId_Evaluat~",
                table: "ContributionPeerEvaluations",
                columns: new[] { "CycleId", "EvaluatorUserId", "EvaluateeUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluations_EvaluateeUserId",
                table: "ContributionPeerEvaluations",
                column: "EvaluateeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPeerEvaluations_EvaluatorUserId",
                table: "ContributionPeerEvaluations",
                column: "EvaluatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionResults_CycleId_UserId",
                table: "ContributionResults",
                columns: new[] { "CycleId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionResults_RunId_CycleId",
                table: "ContributionResults",
                columns: new[] { "RunId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionResults_RunId_UserId",
                table: "ContributionResults",
                columns: new[] { "RunId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionResults_UserId",
                table: "ContributionResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionReviews_ReviewerId",
                table: "ContributionReviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionReviews_RunId",
                table: "ContributionReviews",
                column: "RunId",
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContributionPeerEvaluationHistory");

            migrationBuilder.DropTable(
                name: "ContributionResults");

            migrationBuilder.DropTable(
                name: "ContributionReviews");

            migrationBuilder.DropTable(
                name: "ContributionPeerEvaluations");

            migrationBuilder.DropTable(
                name: "ContributionCalculationRuns");

            migrationBuilder.DropTable(
                name: "ContributionParticipants");

            migrationBuilder.DropTable(
                name: "ContributionCycles");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Projects");
        }
    }
}
