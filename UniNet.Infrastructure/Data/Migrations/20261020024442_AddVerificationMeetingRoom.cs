using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationMeetingRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeetingRoomName",
                table: "ProjectVerifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_MeetingRoomName",
                table: "ProjectVerifications",
                column: "MeetingRoomName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_MeetingRoomName",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "MeetingRoomName",
                table: "ProjectVerifications");
        }
    }
}
