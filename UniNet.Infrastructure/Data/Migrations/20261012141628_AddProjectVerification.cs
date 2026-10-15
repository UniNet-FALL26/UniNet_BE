using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationInDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                    table.CheckConstraint("CK_Packages_DurationInDays", "\"DurationInDays\" IS NULL OR \"DurationInDays\" > 0");
                    table.CheckConstraint("CK_Packages_Price", "\"Price\" >= 0");
                    table.CheckConstraint("CK_Packages_Role", "\"Role\" BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_Packages_Type", "\"Type\" BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "ProjectVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AiSummary = table.Column<string>(type: "text", nullable: true),
                    AiScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScheduledEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MeetingStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MeetingEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVerifications", x => x.Id);
                    table.CheckConstraint("CK_ProjectVerifications_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_ProjectVerifications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectVerifications_Users_FinalizedByUserId",
                        column: x => x.FinalizedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectVerifications_Users_RequestByUserId",
                        column: x => x.RequestByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PaymentUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderOrderCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderPaymentLinkId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_Amount", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_Transactions_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_Transactions_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReportDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignmentDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReminderSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacementReason = table.Column<string>(type: "text", nullable: true),
                    ReplacedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationAssignments", x => x.Id);
                    table.CheckConstraint("CK_VerificationAssignments_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_VerificationAssignments_ProjectVerifications_ProjectVerific~",
                        column: x => x.ProjectVerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerificationAssignments_Users_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationAssignments_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationAssignments_VerificationAssignments_ReplacedById",
                        column: x => x.ReplacedById,
                        principalTable: "VerificationAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefundTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RefundRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundTransactions", x => x.Id);
                    table.CheckConstraint("CK_RefundTransactions_Amount", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_RefundTransactions_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_RefundTransactions_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationTransactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_VerificationTransactions_ProjectVerifications_VerificationId",
                        column: x => x.VerificationId,
                        principalTable: "ProjectVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationTransactions_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerificationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Findings = table.Column<string>(type: "text", nullable: true),
                    Recommendations = table.Column<string>(type: "text", nullable: true),
                    SubmittetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationReports_VerificationAssignments_VerificationId",
                        column: x => x.VerificationId,
                        principalTable: "VerificationAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Code",
                table: "Packages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Type_Role_IsActive",
                table: "Packages",
                columns: new[] { "Type", "Role", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_FinalizedByUserId",
                table: "ProjectVerifications",
                column: "FinalizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_ProjectId_Status",
                table: "ProjectVerifications",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVerifications_RequestByUserId_SubmittedAt",
                table: "ProjectVerifications",
                columns: new[] { "RequestByUserId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefundTransactions_TransactionId_Status",
                table: "RefundTransactions",
                columns: new[] { "TransactionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PackageId",
                table: "Transactions",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProviderOrderCode",
                table: "Transactions",
                column: "ProviderOrderCode");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProviderPaymentLinkId",
                table: "Transactions",
                column: "ProviderPaymentLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status_ExpiredAt",
                table: "Transactions",
                columns: new[] { "Status", "ExpiredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_Status",
                table: "Transactions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_AdminId",
                table: "VerificationAssignments",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_AssignedToUserId_Status",
                table: "VerificationAssignments",
                columns: new[] { "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_AssignmentDueAt",
                table: "VerificationAssignments",
                column: "AssignmentDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_ProjectVerificationId_Status",
                table: "VerificationAssignments",
                columns: new[] { "ProjectVerificationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_ReplacedById",
                table: "VerificationAssignments",
                column: "ReplacedById");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAssignments_ReportDueAt",
                table: "VerificationAssignments",
                column: "ReportDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReports_VerificationId",
                table: "VerificationReports",
                column: "VerificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationTransactions_VerificationId",
                table: "VerificationTransactions",
                column: "VerificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefundTransactions");

            migrationBuilder.DropTable(
                name: "VerificationReports");

            migrationBuilder.DropTable(
                name: "VerificationTransactions");

            migrationBuilder.DropTable(
                name: "VerificationAssignments");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "ProjectVerifications");

            migrationBuilder.DropTable(
                name: "Packages");
        }
    }
}
