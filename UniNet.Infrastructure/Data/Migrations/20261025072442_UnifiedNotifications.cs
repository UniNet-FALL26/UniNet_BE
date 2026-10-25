using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate before any schema mutation. The old constraint normally guarantees one target,
            // but explicit checks also protect databases whose constraints were disabled manually.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "VerificationNotification" n LEFT JOIN "Users" u ON u."Id" = n."RecipientUserId"
                    LEFT JOIN "Accounts" a ON a."Id" = u."AccountId" WHERE a."Id" IS NULL) THEN
                    RAISE EXCEPTION 'Notification recipient cannot be mapped from profile to account; migration aborted.';
                  END IF;
                  IF EXISTS (SELECT 1 FROM "VerificationNotification" WHERE
                    (("VerificationId" IS NOT NULL)::int + ("EvidenceModerationId" IS NOT NULL)::int + ("UserVerificationId" IS NOT NULL)::int) <> 1) THEN
                    RAISE EXCEPTION 'Notification has ambiguous or missing legacy targets; preserve and repair before migration.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_ProjectModerations_EvidenceModerat~",
                table: "VerificationNotification");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_ProjectVerifications_VerificationId",
                table: "VerificationNotification");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_UserVerifications_UserVerification~",
                table: "VerificationNotification");

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_Users_RecipientUserId",
                table: "VerificationNotification");

            migrationBuilder.DropPrimaryKey(
                name: "PK_VerificationNotification",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_EventKey_RecipientUserId",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_EvidenceModerationId_EvidenceModer~",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_VerificationId",
                table: "VerificationNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationNotification_EvidencePurpose",
                table: "VerificationNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification");

            migrationBuilder.DropColumn(
                name: "EvidenceModerationPurpose",
                table: "VerificationNotification");

            migrationBuilder.RenameTable(
                name: "VerificationNotification",
                newName: "Notifications");

            migrationBuilder.RenameColumn(
                name: "VerificationId",
                table: "Notifications",
                newName: "TargetId");

            migrationBuilder.RenameColumn(
                name: "UserVerificationId",
                table: "Notifications",
                newName: "SenderUserId");

            migrationBuilder.RenameColumn(
                name: "EvidenceModerationId",
                table: "Notifications",
                newName: "BroadcastId");

            migrationBuilder.RenameIndex(
                name: "IX_VerificationNotification_UserVerificationId",
                table: "Notifications",
                newName: "IX_Notifications_SenderUserId");

            migrationBuilder.RenameIndex(
                name: "IX_VerificationNotification_RecipientUserId_CreatedAt",
                table: "Notifications",
                newName: "IX_Notifications_RecipientUserId_CreatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "Notifications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "EventKey",
                table: "Notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "Notifications",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Notifications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredPermission",
                table: "Notifications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetType",
                table: "Notifications",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetUrl",
                table: "Notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Notifications",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            // Scaffolded column renames are reused only as temporary carriers of legacy values.
            // Backfill all targets together before resetting SenderUserId/BroadcastId.
            migrationBuilder.Sql("""
                UPDATE "Notifications" n SET
                  "TargetType" = CASE WHEN n."TargetId" IS NOT NULL THEN 'ProjectVerification'
                    WHEN n."SenderUserId" IS NOT NULL THEN 'UserVerification' ELSE 'EvidenceModeration' END,
                  "TargetId" = COALESCE(n."TargetId", n."SenderUserId", n."BroadcastId"),
                  "RecipientUserId" = u."AccountId",
                  "Type" = 'VerificationUpdated', "Category" = 'verification', "Title" = 'Cập nhật xác minh',
                  "Audience" = CASE WHEN a."Role" IN (2,3,4) THEN 'Permission' ELSE 'Personal' END,
                  "RequiredPermission" = CASE WHEN a."Role" IN (2,3,4) THEN
                    CASE WHEN n."SenderUserId" IS NOT NULL THEN 'verification.student.review' ELSE 'verification.evidence.review' END ELSE NULL END,
                  "SenderUserId" = NULL, "BroadcastId" = NULL
                FROM "Users" u JOIN "Accounts" a ON a."Id" = u."AccountId" WHERE u."Id" = n."RecipientUserId";
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Notifications" WHERE "TargetId" IS NULL OR "TargetType" IS NULL OR "Title" = '' OR "Category" = '') THEN
                    RAISE EXCEPTION 'Notification backfill incomplete; migration rolled back.';
                  END IF;
                END $$;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "NotificationBroadcasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TargetUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedCount = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationBroadcasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationBroadcasts_Accounts_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPermissionOverrides",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPermissionOverrides", x => new { x.AccountId, x.Permission });
                    table.ForeignKey(
                        name: "FK_NotificationPermissionOverrides_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationBroadcastRecipients",
                columns: table => new
                {
                    BroadcastId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationBroadcastRecipients", x => new { x.BroadcastId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_NotificationBroadcastRecipients_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationBroadcastRecipients_NotificationBroadcasts_Broa~",
                        column: x => x.BroadcastId,
                        principalTable: "NotificationBroadcasts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BroadcastId",
                table: "Notifications",
                column: "BroadcastId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_EventKey",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "EventKey" },
                unique: true,
                filter: "\"EventKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_ReadAt_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBroadcastRecipients_AccountId",
                table: "NotificationBroadcastRecipients",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBroadcasts_SenderUserId",
                table: "NotificationBroadcasts",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBroadcasts_Status_CreatedAt",
                table: "NotificationBroadcasts",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_AccountId",
                table: "NotificationDeliveries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_DeliveredAt_CreatedAt",
                table: "NotificationDeliveries",
                columns: new[] { "DeliveredAt", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Accounts_RecipientUserId",
                table: "Notifications",
                column: "RecipientUserId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Accounts_SenderUserId",
                table: "Notifications",
                column: "SenderUserId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Notifications" n WHERE n."TargetType" IS NULL OR n."TargetType" NOT IN ('ProjectVerification','EvidenceModeration','UserVerification')
                    OR n."TargetId" IS NULL OR n."EventKey" IS NULL OR length(n."Message") > 2000
                    OR n."SenderUserId" IS NOT NULL OR n."BroadcastId" IS NOT NULL OR n."ReadAt" IS NOT NULL OR n."ExpiresAt" IS NOT NULL
                    OR NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AccountId" = n."RecipientUserId"))
                    OR EXISTS (SELECT 1 FROM "NotificationBroadcasts") OR EXISTS (SELECT 1 FROM "NotificationPermissionOverrides") THEN
                    RAISE EXCEPTION 'Rollback would discard new notification data; export or migrate that data first.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Accounts_RecipientUserId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Accounts_SenderUserId",
                table: "Notifications");

            migrationBuilder.Sql("""
                UPDATE "Notifications" n SET
                  "RecipientUserId" = u."Id",
                  "SenderUserId" = CASE WHEN n."TargetType" = 'UserVerification' THEN n."TargetId" END,
                  "BroadcastId" = CASE WHEN n."TargetType" = 'EvidenceModeration' THEN n."TargetId" END,
                  "TargetId" = CASE WHEN n."TargetType" = 'ProjectVerification' THEN n."TargetId" END
                FROM "Users" u WHERE u."AccountId" = n."RecipientUserId";
                """);

            migrationBuilder.DropTable(
                name: "NotificationBroadcastRecipients");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationPermissionOverrides");

            migrationBuilder.DropTable(
                name: "NotificationBroadcasts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Notifications",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_BroadcastId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RecipientUserId_EventKey",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RecipientUserId_ReadAt_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RequiredPermission",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TargetUrl",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Notifications");

            migrationBuilder.RenameTable(
                name: "Notifications",
                newName: "VerificationNotification");

            migrationBuilder.RenameColumn(
                name: "TargetId",
                table: "VerificationNotification",
                newName: "VerificationId");

            migrationBuilder.RenameColumn(
                name: "SenderUserId",
                table: "VerificationNotification",
                newName: "UserVerificationId");

            migrationBuilder.RenameColumn(
                name: "BroadcastId",
                table: "VerificationNotification",
                newName: "EvidenceModerationId");

            migrationBuilder.RenameIndex(
                name: "IX_Notifications_SenderUserId",
                table: "VerificationNotification",
                newName: "IX_VerificationNotification_UserVerificationId");

            migrationBuilder.RenameIndex(
                name: "IX_Notifications_RecipientUserId_CreatedAt",
                table: "VerificationNotification",
                newName: "IX_VerificationNotification_RecipientUserId_CreatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "VerificationNotification",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "EventKey",
                table: "VerificationNotification",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<short>(
                name: "EvidenceModerationPurpose",
                table: "VerificationNotification",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddPrimaryKey(
                name: "PK_VerificationNotification",
                table: "VerificationNotification",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_EventKey_RecipientUserId",
                table: "VerificationNotification",
                columns: new[] { "EventKey", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_EvidenceModerationId_EvidenceModer~",
                table: "VerificationNotification",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_VerificationId",
                table: "VerificationNotification",
                column: "VerificationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationNotification_EvidencePurpose",
                table: "VerificationNotification",
                sql: "\"EvidenceModerationPurpose\" = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification",
                sql: "(CASE WHEN \"VerificationId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"EvidenceModerationId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"UserVerificationId\" IS NOT NULL THEN 1 ELSE 0 END) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_ProjectModerations_EvidenceModerat~",
                table: "VerificationNotification",
                columns: new[] { "EvidenceModerationId", "EvidenceModerationPurpose" },
                principalTable: "ProjectModerations",
                principalColumns: new[] { "Id", "Purpose" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_ProjectVerifications_VerificationId",
                table: "VerificationNotification",
                column: "VerificationId",
                principalTable: "ProjectVerifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_UserVerifications_UserVerification~",
                table: "VerificationNotification",
                column: "UserVerificationId",
                principalTable: "UserVerifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_Users_RecipientUserId",
                table: "VerificationNotification",
                column: "RecipientUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
