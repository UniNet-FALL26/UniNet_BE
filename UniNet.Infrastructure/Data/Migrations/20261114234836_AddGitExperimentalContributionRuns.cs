using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitExperimentalContributionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitContributionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitEvidenceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunNumber = table.Column<int>(type: "integer", nullable: false),
                    ScoringPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CategoryStatusJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExcludedActivitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReviewHistoryJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CalculatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewFeedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FinalizedById = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitContributionRuns", x => x.Id);
                    table.CheckConstraint("CK_GitContributionRuns_Finalization", "(\"Status\" = 3 AND \"FinalizedAt\" IS NOT NULL AND \"FinalizedById\" IS NOT NULL) OR (\"Status\" <> 3 AND \"FinalizedAt\" IS NULL AND \"FinalizedById\" IS NULL)");
                    table.CheckConstraint("CK_GitContributionRuns_Hash", "length(\"InputHash\") = 64");
                    table.CheckConstraint("CK_GitContributionRuns_Policy", "\"ScoringPolicyVersion\" = 'ExperimentalGitScoringV1'");
                    table.CheckConstraint("CK_GitContributionRuns_RunNumber", "\"RunNumber\" > 0");
                    table.CheckConstraint("CK_GitContributionRuns_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_GitContributionRuns_ProjectGitEvidenceSnapshots_GitEvidence~",
                        columns: x => new { x.GitEvidenceSnapshotId, x.ProjectId },
                        principalTable: "ProjectGitEvidenceSnapshots",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionRuns_Users_CalculatedById",
                        column: x => x.CalculatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionRuns_Users_FinalizedById",
                        column: x => x.FinalizedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitContributionRuns_Users_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_CalculatedById",
                table: "GitContributionRuns",
                column: "CalculatedById");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_FinalizedById",
                table: "GitContributionRuns",
                column: "FinalizedById");

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_GitEvidenceSnapshotId_ProjectId",
                table: "GitContributionRuns",
                columns: new[] { "GitEvidenceSnapshotId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~",
                table: "GitContributionRuns",
                columns: new[] { "ProjectId", "GitEvidenceSnapshotId", "ScoringPolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_ProjectId_RunNumber",
                table: "GitContributionRuns",
                columns: new[] { "ProjectId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitContributionRuns_ReviewedById",
                table: "GitContributionRuns",
                column: "ReviewedById");

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_finalized_git_contribution_run_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF OLD."Status" = 3 THEN
                        RAISE EXCEPTION 'Finalized Git contribution runs are immutable'
                            USING ERRCODE = '55000';
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "TR_GitContributionRuns_FinalizedImmutable"
                BEFORE UPDATE OR DELETE ON "GitContributionRuns"
                FOR EACH ROW
                EXECUTE FUNCTION prevent_finalized_git_contribution_run_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_GitContributionRuns_FinalizedImmutable" ON "GitContributionRuns";
                DROP FUNCTION IF EXISTS prevent_finalized_git_contribution_run_mutation();
                """);
            migrationBuilder.DropTable(
                name: "GitContributionRuns");
        }
    }
}
