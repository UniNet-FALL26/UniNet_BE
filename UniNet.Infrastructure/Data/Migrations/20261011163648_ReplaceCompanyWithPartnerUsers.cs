using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCompanyWithPartnerUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Company_CompanyUserId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "CompanySavedStudents");

            migrationBuilder.DropTable(
                name: "Company");

            migrationBuilder.RenameColumn(
                name: "CompanyUserId",
                table: "Jobs",
                newName: "PartnerId");

            migrationBuilder.RenameIndex(
                name: "IX_Jobs_CompanyUserId",
                table: "Jobs",
                newName: "IX_Jobs_PartnerId");

            migrationBuilder.CreateTable(
                name: "User1SavedUser2",
                columns: table => new
                {
                    UserBookmarkId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User1SavedUser2", x => new { x.UserBookmarkId, x.UserId });
                    table.ForeignKey(
                        name: "FK_User1SavedUser2_Users_UserBookmarkId",
                        column: x => x.UserBookmarkId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_User1SavedUser2_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_User1SavedUser2_UserId",
                table: "User1SavedUser2",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Users_PartnerId",
                table: "Jobs",
                column: "PartnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Users_PartnerId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "User1SavedUser2");

            migrationBuilder.RenameColumn(
                name: "PartnerId",
                table: "Jobs",
                newName: "CompanyUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Jobs_PartnerId",
                table: "Jobs",
                newName: "IX_Jobs_CompanyUserId");

            migrationBuilder.CreateTable(
                name: "Company",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CoverUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Website = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Company", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Company_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanySavedStudents",
                columns: table => new
                {
                    CompanyUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySavedStudents", x => new { x.CompanyUserId, x.StudentUserId });
                    table.ForeignKey(
                        name: "FK_CompanySavedStudents_Company_CompanyUserId",
                        column: x => x.CompanyUserId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanySavedStudents_Users_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Company_AccountId",
                table: "Company",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySavedStudents_StudentUserId",
                table: "CompanySavedStudents",
                column: "StudentUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Company_CompanyUserId",
                table: "Jobs",
                column: "CompanyUserId",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
