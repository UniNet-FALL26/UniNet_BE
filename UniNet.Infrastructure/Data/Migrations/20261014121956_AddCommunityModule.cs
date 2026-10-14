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
            migrationBuilder.AlterColumn<Guid>(
                name: "FinalizedByUserId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "AiAdditionalEvidenceJson",
                table: "ProjectVerifications",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AiEvidenceVersion",
                table: "ProjectVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiReviewedAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "ProjectVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "ProjectVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

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
                name: "ProjectVerificationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVerificationEvidence", x => x.Id);
                    table.CheckConstraint("CK_ProjectVerificationEvidence_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ProjectVerificationEvidence_ProjectVerifications_ProjectVer~",
                        column: x => x.ProjectVerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectVerificationEvidence_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "IX_ProjectVerifications_PackageId",
                table: "ProjectVerifications",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_PendingProject",
                table: "ProjectVerifications",
                column: "ProjectId",
                unique: true,
                filter: "\"Status\" = 0");

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

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerificationEvidence_ProjectVerificationId_Version",
                table: "ProjectVerificationEvidence",
                columns: new[] { "ProjectVerificationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerificationEvidence_SubmittedByUserId",
                table: "ProjectVerificationEvidence",
                column: "SubmittedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectVerifications_Packages_PackageId",
                table: "ProjectVerifications",
                column: "PackageId",
                principalTable: "Packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectVerifications_Packages_PackageId",
                table: "ProjectVerifications");

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
                name: "ProjectVerificationEvidence");

            migrationBuilder.DropTable(
                name: "CommunityPosts");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_PackageId",
                table: "ProjectVerifications");

            migrationBuilder.DropIndex(
                name: "IX_ProjectVerifications_PendingProject",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiAdditionalEvidenceJson",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiEvidenceVersion",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AiReviewedAt",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "ProjectVerifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "FinalizedByUserId",
                table: "ProjectVerifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
