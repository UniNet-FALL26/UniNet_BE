using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessorOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessorOnboardings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SensitiveProfileCiphertext = table.Column<string>(type: "text", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AutoApprove = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AutoApproveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProfileTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProfileTokenUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContractTokenHash = table.Column<string>(type: "text", nullable: true),
                    ContractTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SetPasswordTokenHash = table.Column<string>(type: "text", nullable: true),
                    SetPasswordTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SetPasswordTokenUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TemplateVersion = table.Column<string>(type: "text", nullable: true),
                    ContractStorageKey = table.Column<string>(type: "text", nullable: true),
                    ContractSha256 = table.Column<string>(type: "text", nullable: true),
                    SignedName = table.Column<string>(type: "text", nullable: true),
                    SignatureStatement = table.Column<string>(type: "text", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OtpHash = table.Column<string>(type: "text", nullable: true),
                    OtpPurpose = table.Column<string>(type: "text", nullable: true),
                    OtpExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OtpLastSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OtpFailedAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessorOnboardings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessorOnboardings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessorOnboardings_Accounts_CreatedByAccountId",
                        column: x => x.CreatedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessorOnboardings_Accounts_ReviewedByAccountId",
                        column: x => x.ReviewedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessorOnboardingEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OnboardingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "text", nullable: false),
                    PayloadCiphertext = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessorOnboardingEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessorOnboardingEmails_AssessorOnboardings_OnboardingId",
                        column: x => x.OnboardingId,
                        principalTable: "AssessorOnboardings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardingEmails_DeduplicationKey",
                table: "AssessorOnboardingEmails",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardingEmails_NextAttemptAt",
                table: "AssessorOnboardingEmails",
                column: "NextAttemptAt",
                filter: "\"SentAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardingEmails_OnboardingId",
                table: "AssessorOnboardingEmails",
                column: "OnboardingId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_AccountId",
                table: "AssessorOnboardings",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_CreatedByAccountId",
                table: "AssessorOnboardings",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_ProfileTokenHash",
                table: "AssessorOnboardings",
                column: "ProfileTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_ReviewedByAccountId",
                table: "AssessorOnboardings",
                column: "ReviewedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessorOnboardings_Status_AutoApproveAt",
                table: "AssessorOnboardings",
                columns: new[] { "Status", "AutoApproveAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AssessorOnboardings_OpenEmail",
                table: "AssessorOnboardings",
                column: "EmailNormalized",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3, 4, 9)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessorOnboardingEmails");

            migrationBuilder.DropTable(
                name: "AssessorOnboardings");
        }
    }
}
