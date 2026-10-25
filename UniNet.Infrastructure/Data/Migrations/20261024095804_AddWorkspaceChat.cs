using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceChannels", x => x.Id);
                    table.CheckConstraint("CK_WorkspaceChannels_DeletedMetadata", "(\"IsDeleted\" = FALSE AND \"DeletedAt\" IS NULL AND \"DeletedById\" IS NULL) OR (\"IsDeleted\" = TRUE AND \"DeletedAt\" IS NOT NULL AND \"DeletedById\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WorkspaceChannels_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspaceChannels_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceChannels_Users_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMessages_Users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceMessages_WorkspaceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "WorkspaceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceChannels_CreatedById",
                table: "WorkspaceChannels",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceChannels_DeletedById",
                table: "WorkspaceChannels",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceChannels_ProjectId_IsDeleted",
                table: "WorkspaceChannels",
                columns: new[] { "ProjectId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMessages_ChannelId_CreatedAt_Id",
                table: "WorkspaceMessages",
                columns: new[] { "ChannelId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMessages_SenderId",
                table: "WorkspaceMessages",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceMessages");

            migrationBuilder.DropTable(
                name: "WorkspaceChannels");
        }
    }
}
