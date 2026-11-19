using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowPersonalProjectLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_RecruitmentApplicability",
                table: "Projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_RecruitmentApplicability",
                table: "Projects",
                sql: "(\"Origin\" = 0 AND \"MemberTarget\" IS NOT NULL AND \"MinimumQuantity\" IS NOT NULL AND \"RecruitmentDeadline\" IS NOT NULL) OR (\"Origin\" = 1 AND \"MemberTarget\" IS NULL AND \"MinimumQuantity\" IS NULL AND \"RecruitmentDeadline\" IS NULL AND \"RecruitmentStatus\" = 2 AND \"Status\" = 2)");
        }
    }
}
