using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommunityPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorActorType = table.Column<short>(type: "smallint", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    MediaUrlsJson = table.Column<string>(type: "jsonb", nullable: true),
                    FavoriteCount = table.Column<int>(type: "integer", nullable: false),
                    CommentCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityPosts", x => x.Id);
                    table.CheckConstraint("CK_CommunityPosts_AuthorActorType", "\"AuthorActorType\" BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_CommunityPosts_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_CommunityPosts_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommunityPostBookmarks",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityPostBookmarks", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CommunityPostBookmarks_CommunityPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "CommunityPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityPostBookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommunityPostComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplyToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityPostComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityPostComments_CommunityPostComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "CommunityPostComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityPostComments_CommunityPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "CommunityPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityPostComments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityPostComments_Users_ReplyToUserId",
                        column: x => x.ReplyToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommunityPostMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<short>(type: "smallint", nullable: false),
                    FileUrl = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityPostMedia", x => x.Id);
                    table.CheckConstraint("CK_CommunityPostMedia_MediaType", "\"MediaType\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_CommunityPostMedia_CommunityPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "CommunityPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommunityPostReactions",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityPostReactions", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CommunityPostReactions_CommunityPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "CommunityPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityPostReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReports", x => x.Id);
                    table.CheckConstraint("CK_ContentReports_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ContentReports_CommunityPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "CommunityPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostBookmarks_UserId",
                table: "CommunityPostBookmarks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostComments_AuthorUserId",
                table: "CommunityPostComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostComments_ParentCommentId",
                table: "CommunityPostComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostComments_PostId",
                table: "CommunityPostComments",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostComments_ReplyToUserId",
                table: "CommunityPostComments",
                column: "ReplyToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostMedia_PostId",
                table: "CommunityPostMedia",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostReactions_UserId",
                table: "CommunityPostReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPosts_AuthorUserId",
                table: "CommunityPosts",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_PostId",
                table: "ContentReports",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReporterUserId",
                table: "ContentReports",
                column: "ReporterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityPostBookmarks");

            migrationBuilder.DropTable(
                name: "CommunityPostComments");

            migrationBuilder.DropTable(
                name: "CommunityPostMedia");

            migrationBuilder.DropTable(
                name: "CommunityPostReactions");

            migrationBuilder.DropTable(
                name: "ContentReports");

            migrationBuilder.DropTable(
                name: "CommunityPosts");
        }
    }
}
