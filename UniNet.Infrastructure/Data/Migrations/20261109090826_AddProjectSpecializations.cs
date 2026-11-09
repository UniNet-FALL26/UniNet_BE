using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSpecializations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SpecializationId",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Specializations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Specializations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssessorSpecializations",
                columns: table => new
                {
                    AssessorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecializationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessorSpecializations", x => new { x.AssessorUserId, x.SpecializationId });
                    table.ForeignKey(
                        name: "FK_AssessorSpecializations_Specializations_SpecializationId",
                        column: x => x.SpecializationId,
                        principalTable: "Specializations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessorSpecializations_Users_AssessorUserId",
                        column: x => x.AssessorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Specializations",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("a4900000-0000-0000-0000-000000000001"), "SOFTWARE_ENGINEERING", true, "Kỹ thuật phần mềm" },
                    { new Guid("a4900000-0000-0000-0000-000000000002"), "ARTIFICIAL_INTELLIGENCE", true, "Trí tuệ nhân tạo" },
                    { new Guid("a4900000-0000-0000-0000-000000000003"), "INFORMATION_SECURITY", true, "An toàn thông tin" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SpecializationId",
                table: "Projects",
                column: "SpecializationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessorSpecializations_SpecializationId",
                table: "AssessorSpecializations",
                column: "SpecializationId");

            migrationBuilder.CreateIndex(
                name: "IX_Specializations_Code",
                table: "Specializations",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Specializations_SpecializationId",
                table: "Projects",
                column: "SpecializationId",
                principalTable: "Specializations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Specializations_SpecializationId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "AssessorSpecializations");

            migrationBuilder.DropTable(
                name: "Specializations");

            migrationBuilder.DropIndex(
                name: "IX_Projects_SpecializationId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SpecializationId",
                table: "Projects");
        }
    }
}
