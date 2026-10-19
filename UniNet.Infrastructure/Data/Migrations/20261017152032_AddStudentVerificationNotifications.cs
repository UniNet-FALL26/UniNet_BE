using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentVerificationNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification");

            migrationBuilder.AddColumn<Guid>(
                name: "UserVerificationId",
                table: "VerificationNotification",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationNotification_UserVerificationId",
                table: "VerificationNotification",
                column: "UserVerificationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification",
                sql: "(CASE WHEN \"VerificationId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"EvidenceModerationId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"UserVerificationId\" IS NOT NULL THEN 1 ELSE 0 END) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationNotification_UserVerifications_UserVerification~",
                table: "VerificationNotification",
                column: "UserVerificationId",
                principalTable: "UserVerifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse rollback before changing the schema if doing so would discard student notification history.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "VerificationNotification" WHERE "UserVerificationId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back student verification notifications while history exists; preserve or migrate those notifications first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_VerificationNotification_UserVerifications_UserVerification~",
                table: "VerificationNotification");

            migrationBuilder.DropIndex(
                name: "IX_VerificationNotification_UserVerificationId",
                table: "VerificationNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification");

            migrationBuilder.DropColumn(
                name: "UserVerificationId",
                table: "VerificationNotification");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VerificationNotification_Target",
                table: "VerificationNotification",
                sql: "(\"VerificationId\" IS NOT NULL AND \"EvidenceModerationId\" IS NULL) OR (\"VerificationId\" IS NULL AND \"EvidenceModerationId\" IS NOT NULL)");
        }
    }
}
