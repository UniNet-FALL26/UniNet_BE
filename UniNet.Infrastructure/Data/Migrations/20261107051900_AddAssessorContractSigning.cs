using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessorContractSigning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssessorOnboardingEmails_OnboardingId",
                table: "AssessorOnboardingEmails");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContractViewedAt",
                table: "AssessorOnboardings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GenerationAttempts",
                table: "AssessorOnboardings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GenerationErrorCode",
                table: "AssessorOnboardings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GenerationRetryAt",
                table: "AssessorOnboardings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadSessionExpiresAt",
                table: "AssessorOnboardings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadSessionHash",
                table: "AssessorOnboardings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Revision",
                table: "AssessorOnboardings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "AssessorOnboardingEmails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AssessorOnboardingEmails",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CredentialHash",
                table: "AssessorOnboardingEmails",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "AssessorOnboardingEmails",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "AssessorOnboardingEmails",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_ContractTokenHash",
                table: "AssessorOnboardings",
                column: "ContractTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_ReadSessionHash",
                table: "AssessorOnboardings",
                column: "ReadSessionHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_Status_GenerationRetryAt",
                table: "AssessorOnboardings",
                columns: new[] { "Status", "GenerationRetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardingEmails_OnboardingId_Kind_CreatedAt",
                table: "AssessorOnboardingEmails",
                columns: new[] { "OnboardingId", "Kind", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssessorOnboardings_ContractTokenHash",
                table: "AssessorOnboardings");

            migrationBuilder.DropIndex(
                name: "IX_AssessorOnboardings_ReadSessionHash",
                table: "AssessorOnboardings");

            migrationBuilder.DropIndex(
                name: "IX_AssessorOnboardings_Status_GenerationRetryAt",
                table: "AssessorOnboardings");

            migrationBuilder.DropIndex(
                name: "IX_AssessorOnboardingEmails_OnboardingId_Kind_CreatedAt",
                table: "AssessorOnboardingEmails");

            migrationBuilder.DropColumn(
                name: "ContractViewedAt",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "GenerationAttempts",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "GenerationErrorCode",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "GenerationRetryAt",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "ReadSessionExpiresAt",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "ReadSessionHash",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "AssessorOnboardings");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "AssessorOnboardingEmails");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AssessorOnboardingEmails");

            migrationBuilder.DropColumn(
                name: "CredentialHash",
                table: "AssessorOnboardingEmails");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "AssessorOnboardingEmails");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "AssessorOnboardingEmails");

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardingEmails_OnboardingId",
                table: "AssessorOnboardingEmails",
                column: "OnboardingId");
        }
    }
}
