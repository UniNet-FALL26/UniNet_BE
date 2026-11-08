using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessorPasswordTokenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_SetPasswordTokenHash",
                table: "AssessorOnboardings",
                column: "SetPasswordTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssessorOnboardings_SetPasswordTokenHash",
                table: "AssessorOnboardings");
        }
    }
}
