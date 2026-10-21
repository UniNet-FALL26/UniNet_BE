using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameUserBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "User1SavedUser2", newName: "UserBookmarks");
            migrationBuilder.RenameColumn(name: "UserId", table: "UserBookmarks", newName: "BookmarkedUserId");
            migrationBuilder.RenameColumn(name: "UserBookmarkId", table: "UserBookmarks", newName: "UserId");

            migrationBuilder.Sql("ALTER TABLE \"UserBookmarks\" RENAME CONSTRAINT \"PK_User1SavedUser2\" TO \"PK_UserBookmarks\";");
            migrationBuilder.Sql("ALTER TABLE \"UserBookmarks\" RENAME CONSTRAINT \"FK_User1SavedUser2_Users_UserBookmarkId\" TO \"FK_UserBookmarks_Users_UserId\";");
            migrationBuilder.Sql("ALTER TABLE \"UserBookmarks\" RENAME CONSTRAINT \"FK_User1SavedUser2_Users_UserId\" TO \"FK_UserBookmarks_Users_BookmarkedUserId\";");
            migrationBuilder.RenameIndex(name: "IX_User1SavedUser2_UserId", table: "UserBookmarks", newName: "IX_UserBookmarks_BookmarkedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(name: "IX_UserBookmarks_BookmarkedUserId", table: "UserBookmarks", newName: "IX_User1SavedUser2_UserId");
            migrationBuilder.Sql("ALTER TABLE \"UserBookmarks\" RENAME CONSTRAINT \"FK_UserBookmarks_Users_BookmarkedUserId\" TO \"FK_User1SavedUser2_Users_UserId\";");
            migrationBuilder.Sql("ALTER TABLE \"UserBookmarks\" RENAME CONSTRAINT \"FK_UserBookmarks_Users_UserId\" TO \"FK_User1SavedUser2_Users_UserBookmarkId\";");
            migrationBuilder.Sql("ALTER TABLE \"UserBookmarks\" RENAME CONSTRAINT \"PK_UserBookmarks\" TO \"PK_User1SavedUser2\";");

            migrationBuilder.RenameColumn(name: "UserId", table: "UserBookmarks", newName: "UserBookmarkId");
            migrationBuilder.RenameColumn(name: "BookmarkedUserId", table: "UserBookmarks", newName: "UserId");
            migrationBuilder.RenameTable(name: "UserBookmarks", newName: "User1SavedUser2");
        }
    }
}
