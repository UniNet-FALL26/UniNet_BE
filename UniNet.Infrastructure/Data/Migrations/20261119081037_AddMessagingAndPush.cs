using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingAndPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "NotificationDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                table: "NotificationDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PushDeviceId",
                table: "NotificationDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountBlocks",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBlocks", x => new { x.AccountId, x.BlockedAccountId });
                    table.CheckConstraint("CK_AccountBlocks_NotSelf", "\"AccountId\" <> \"BlockedAccountId\"");
                    table.ForeignKey(
                        name: "FK_AccountBlocks_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountBlocks_Accounts_BlockedAccountId",
                        column: x => x.BlockedAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    DirectAccountLowId = table.Column<Guid>(type: "uuid", nullable: true),
                    DirectAccountHighId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextMessageNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.CheckConstraint("CK_Conversations_DirectPair", "(\"Type\" = 0 AND \"DirectAccountLowId\" IS NOT NULL AND \"DirectAccountHighId\" IS NOT NULL AND \"DirectAccountLowId\" < \"DirectAccountHighId\" AND \"ProjectId\" IS NULL) OR (\"Type\" = 1 AND \"DirectAccountLowId\" IS NULL AND \"DirectAccountHighId\" IS NULL)");
                    table.CheckConstraint("CK_Conversations_Type", "\"Type\" IN (0,1)");
                    table.ForeignKey(
                        name: "FK_Conversations_Accounts_CreatedByAccountId",
                        column: x => x.CreatedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversations_Accounts_DirectAccountHighId",
                        column: x => x.DirectAccountHighId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversations_Accounts_DirectAccountLowId",
                        column: x => x.DirectAccountLowId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Token = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Platform = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    SessionVersion = table.Column<int>(type: "integer", nullable: false),
                    ShowPreview = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushDevices_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatDeviceSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActiveConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsForeground = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatDeviceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatDeviceSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatDeviceSessions_Conversations_ActiveConversationId",
                        column: x => x.ActiveConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMembers",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReadMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    MutedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMembers", x => new { x.ConversationId, x.AccountId });
                    table.CheckConstraint("CK_ConversationMembers_Role", "\"Role\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ConversationMembers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConversationMembers_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    ReplyToMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.UniqueConstraint("AK_Messages_ConversationId_Id", x => new { x.ConversationId, x.Id });
                    table.CheckConstraint("CK_Messages_Number", "\"Number\" > 0");
                    table.CheckConstraint("CK_Messages_Type", "\"Type\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_Messages_ConversationMembers_ConversationId_SenderAccountId",
                        columns: x => new { x.ConversationId, x.SenderAccountId },
                        principalTable: "ConversationMembers",
                        principalColumns: new[] { "ConversationId", "AccountId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Messages_ConversationId_ReplyToMessageId",
                        columns: x => new { x.ConversationId, x.ReplyToMessageId },
                        principalTable: "Messages",
                        principalColumns: new[] { "ConversationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileType = table.Column<short>(type: "smallint", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageAttachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageReactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReactionType = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageReactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MessageReactions_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_ConversationId",
                table: "NotificationDeliveries",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_MessageId",
                table: "NotificationDeliveries",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_PushDeviceId",
                table: "NotificationDeliveries",
                column: "PushDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Reason_DeliveredAt_RetryAt",
                table: "NotificationDeliveries",
                columns: new[] { "Reason", "DeliveredAt", "RetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBlocks_BlockedAccountId",
                table: "AccountBlocks",
                column: "BlockedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatDeviceSessions_AccountId_DeviceId",
                table: "ChatDeviceSessions",
                columns: new[] { "AccountId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatDeviceSessions_ActiveConversationId",
                table: "ChatDeviceSessions",
                column: "ActiveConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatDeviceSessions_UpdatedAt",
                table: "ChatDeviceSessions",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_AccountId_LeftAt",
                table: "ConversationMembers",
                columns: new[] { "AccountId", "LeftAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_ActiveOwner",
                table: "ConversationMembers",
                column: "ConversationId",
                unique: true,
                filter: "\"Role\" = 2 AND \"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMembers_ConversationId_LastReadMessageId",
                table: "ConversationMembers",
                columns: new[] { "ConversationId", "LastReadMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CreatedByAccountId",
                table: "Conversations",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_DirectAccountHighId",
                table: "Conversations",
                column: "DirectAccountHighId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_DirectAccountLowId_DirectAccountHighId",
                table: "Conversations",
                columns: new[] { "DirectAccountLowId", "DirectAccountHighId" },
                unique: true,
                filter: "\"Type\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_LastMessageAt",
                table: "Conversations",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProjectId",
                table: "Conversations",
                column: "ProjectId",
                unique: true,
                filter: "\"ProjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_MessageId",
                table: "MessageAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageReactions_AccountId",
                table: "MessageReactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageReactions_MessageId_AccountId",
                table: "MessageReactions",
                columns: new[] { "MessageId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_CreatedAt_Id",
                table: "Messages",
                columns: new[] { "ConversationId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_Number",
                table: "Messages",
                columns: new[] { "ConversationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_ReplyToMessageId",
                table: "Messages",
                columns: new[] { "ConversationId", "ReplyToMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SenderAccountId",
                table: "Messages",
                columns: new[] { "ConversationId", "SenderAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderAccountId_ClientMessageId",
                table: "Messages",
                columns: new[] { "SenderAccountId", "ClientMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_AccountId_DeviceId",
                table: "PushDevices",
                columns: new[] { "AccountId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_Token",
                table: "PushDevices",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationDeliveries_Conversations_ConversationId",
                table: "NotificationDeliveries",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationDeliveries_Messages_MessageId",
                table: "NotificationDeliveries",
                column: "MessageId",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationDeliveries_PushDevices_PushDeviceId",
                table: "NotificationDeliveries",
                column: "PushDeviceId",
                principalTable: "PushDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMembers_Messages_ConversationId_LastReadMessage~",
                table: "ConversationMembers",
                columns: new[] { "ConversationId", "LastReadMessageId" },
                principalTable: "Messages",
                principalColumns: new[] { "ConversationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationDeliveries_Conversations_ConversationId",
                table: "NotificationDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationDeliveries_Messages_MessageId",
                table: "NotificationDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationDeliveries_PushDevices_PushDeviceId",
                table: "NotificationDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMembers_Conversations_ConversationId",
                table: "ConversationMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Conversations_ConversationId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMembers_Messages_ConversationId_LastReadMessage~",
                table: "ConversationMembers");

            migrationBuilder.DropTable(
                name: "AccountBlocks");

            migrationBuilder.DropTable(
                name: "ChatDeviceSessions");

            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropTable(
                name: "MessageReactions");

            migrationBuilder.DropTable(
                name: "PushDevices");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "ConversationMembers");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_ConversationId",
                table: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_MessageId",
                table: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_PushDeviceId",
                table: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_Reason_DeliveredAt_RetryAt",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "PushDeviceId",
                table: "NotificationDeliveries");
        }
    }
}
