using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreProjectGitMemberIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectGitMemberIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    VerifiedByConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnlinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitMemberIdentities", x => x.Id);
                    table.CheckConstraint("CK_ProjectGitMemberIdentities_ExternalAccountId", "\"ExternalAccountId\" > 0");
                    table.CheckConstraint("CK_ProjectGitMemberIdentities_Status", "\"Status\" BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_ProjectGitMemberIdentities_UnlinkedAt", "(\"Status\" = 0 AND \"UnlinkedAt\" IS NULL) OR (\"Status\" = 1 AND \"UnlinkedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentities_GitHubOAuthConnections_VerifiedB~",
                        column: x => x.VerifiedByConnectionId,
                        principalTable: "GitHubOAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentities_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentities_Users_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGitMemberIdentityHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PreviousExternalUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Action = table.Column<short>(type: "smallint", nullable: false),
                    PreviousStatus = table.Column<short>(type: "smallint", nullable: true),
                    NewStatus = table.Column<short>(type: "smallint", nullable: true),
                    PerformedByProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedByConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGitMemberIdentityHistory", x => x.Id);
                    table.CheckConstraint("CK_ProjectGitMemberIdentityHistory_Action", "\"Action\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_ProjectGitMemberIdentityHistory_ExternalAccountId", "\"ExternalAccountId\" > 0");
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentityHistory_GitHubOAuthConnections_Veri~",
                        column: x => x.VerifiedByConnectionId,
                        principalTable: "GitHubOAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentityHistory_ProjectGitMemberIdentities_~",
                        column: x => x.IdentityId,
                        principalTable: "ProjectGitMemberIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentityHistory_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentityHistory_Users_PerformedByProfileId",
                        column: x => x.PerformedByProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectGitMemberIdentityHistory_Users_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentities_ProjectId_Provider_ExternalAccou~",
                table: "ProjectGitMemberIdentities",
                columns: new[] { "ProjectId", "Provider", "ExternalAccountId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentities_ProjectId_UserProfileId_Status",
                table: "ProjectGitMemberIdentities",
                columns: new[] { "ProjectId", "UserProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentities_UserProfileId",
                table: "ProjectGitMemberIdentities",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentities_VerifiedByConnectionId",
                table: "ProjectGitMemberIdentities",
                column: "VerifiedByConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentityHistory_IdentityId_OccurredAt",
                table: "ProjectGitMemberIdentityHistory",
                columns: new[] { "IdentityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentityHistory_PerformedByProfileId",
                table: "ProjectGitMemberIdentityHistory",
                column: "PerformedByProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentityHistory_ProjectId_OccurredAt",
                table: "ProjectGitMemberIdentityHistory",
                columns: new[] { "ProjectId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentityHistory_UserProfileId",
                table: "ProjectGitMemberIdentityHistory",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGitMemberIdentityHistory_VerifiedByConnectionId",
                table: "ProjectGitMemberIdentityHistory",
                column: "VerifiedByConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectGitMemberIdentityHistory");

            migrationBuilder.DropTable(
                name: "ProjectGitMemberIdentities");
        }
    }
}
