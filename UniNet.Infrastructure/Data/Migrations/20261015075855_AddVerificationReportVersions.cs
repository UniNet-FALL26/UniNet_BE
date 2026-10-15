using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationReportVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "Outcome",
                table: "VerificationReports",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                table: "VerificationReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "VerificationAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportAuditLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAuditLogs_VerificationReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "VerificationReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationReportVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Findings = table.Column<string>(type: "text", nullable: false),
                    Recommendations = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationReportVersions", x => x.Id);
                    table.CheckConstraint("CK_ReportVersions_Outcome", "\"Outcome\" IN (1, 2)");
                    table.CheckConstraint("CK_ReportVersions_Version", "\"Version\" >= 2");
                    table.ForeignKey(
                        name: "FK_VerificationReportVersions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerificationReportVersions_VerificationReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "VerificationReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReports_SubmittedByUserId",
                table: "VerificationReports",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAuditLogs_ActorUserId",
                table: "ReportAuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAuditLogs_ReportId_Version",
                table: "ReportAuditLogs",
                columns: new[] { "ReportId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReportVersions_ReportId_Version",
                table: "VerificationReportVersions",
                columns: new[] { "ReportId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReportVersions_SubmittedByUserId",
                table: "VerificationReportVersions",
                column: "SubmittedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationReports_Users_SubmittedByUserId",
                table: "VerificationReports",
                column: "SubmittedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VerificationReports_Users_SubmittedByUserId",
                table: "VerificationReports");

            migrationBuilder.DropTable(
                name: "ReportAuditLogs");

            migrationBuilder.DropTable(
                name: "VerificationReportVersions");

            migrationBuilder.DropIndex(
                name: "IX_VerificationReports_SubmittedByUserId",
                table: "VerificationReports");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "VerificationReports");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "VerificationReports");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "VerificationAssignments");
        }
    }
}
