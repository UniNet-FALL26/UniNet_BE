using Microsoft.EntityFrameworkCore.Migrations;

namespace UniNet.Infrastructure.Data.Migrations;

public partial class EnableProjectHubModeration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => UpdateConstraints(migrationBuilder, includeHub: true);

    protected override void Down(MigrationBuilder migrationBuilder) => UpdateConstraints(migrationBuilder, includeHub: false);

    private static void UpdateConstraints(MigrationBuilder migrationBuilder, bool includeHub)
    {
        const string table = "ProjectModerations";
        const string purposeConstraint = "CK_ProjectModerations_Purpose";
        const string statusConstraint = "CK_ProjectModerations_Status";
        var submissionPurpose = includeHub ? "\"Purpose\" IN (0, 2)" : "\"Purpose\" = 0";

        migrationBuilder.DropCheckConstraint(purposeConstraint, table);
        migrationBuilder.DropCheckConstraint(statusConstraint, table);
        migrationBuilder.AddCheckConstraint(purposeConstraint, table, submissionPurpose);
        migrationBuilder.AddCheckConstraint(statusConstraint, table,
            $"({submissionPurpose} AND \"Status\" BETWEEN 0 AND 5) OR (\"Purpose\" = 1 AND \"Status\" IN (0,1,4,5,6,7))");
    }
}
