using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuardGitV2FinalNormalizationReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_GitContributionRuns_V2FinalReady",
                table: "GitContributionRuns",
                sql: "\"ScoringPolicyVersion\" <> 'ExperimentalGitScoringV2' OR \"Status\" <> 3 OR (\"CalculationStatus\" = 'Ready' AND \"CalculationStatus\" IS NOT NULL AND \"NormalizedResultsJson\" IS NOT NULL AND \"NormalizationPolicyVersion\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GitContributionRuns_V2FinalReady",
                table: "GitContributionRuns");
        }
    }
}
