using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificatorAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservedVerificatorUserId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerificatorAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificatorAvailabilities", x => x.Id);
                    table.CheckConstraint("CK_VerificatorAvailabilities_Interval", "\"EndAt\" > \"StartAt\"");
                    table.ForeignKey(
                        name: "FK_VerificatorAvailabilities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_ReservedVerificatorUserId",
                table: "ProjectVerifications",
                column: "ReservedVerificatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificatorAvailabilities_UserId_StartAt_EndAt",
                table: "VerificatorAvailabilities",
                columns: new[] { "UserId", "StartAt", "EndAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_Users_ReservedVerificatorUserId",
                table: "ProjectVerifications",
                column: "ReservedVerificatorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Preserve the resource already assigned to historical bookings; do not fabricate availability.
            migrationBuilder.Sql("""
                UPDATE "ProjectVerifications" v
                SET "ReservedVerificatorUserId" = (
                    SELECT a."AssignedToUserId" FROM "VerificationAssignments" a
                    WHERE a."ProjectVerificationId" = v."Id" AND a."Status" IN (0, 2)
                    ORDER BY a."AssignedAt" DESC, a."Id" LIMIT 1)
                WHERE EXISTS (
                    SELECT 1 FROM "VerificationAssignments" a
                    WHERE a."ProjectVerificationId" = v."Id" AND a."Status" IN (0, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_Users_ReservedVerificatorUserId",
                table: "ProjectVerifications");

            migrationBuilder.DropTable(
                name: "VerificatorAvailabilities");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_ReservedVerificatorUserId",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "ReservedVerificatorUserId",
                table: "ProjectVerifications");
        }
    }
}
