using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalProjectConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RecruitmentDeadline",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "MinimumQuantity",
                table: "Projects",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "MemberTarget",
                table: "Projects",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ExternalDetailsJson",
                table: "Projects",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalLeaderName",
                table: "Projects",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImplementationEndDate",
                table: "Projects",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImplementationStartDate",
                table: "Projects",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "Origin",
                table: "Projects",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SharingRightsConfirmedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmitterContribution",
                table: "Projects",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SubmitterIsLeader",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SubmitterRole",
                table: "Projects",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ExternalProjectDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "ExternalProjectDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "ExternalProjectDrafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "ExternalProjectDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectEvidenceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEvidenceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceDocuments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectEvidenceDocuments_UploadedFiles_UploadedFileId",
                        column: x => x.UploadedFileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Origin",
                table: "Projects",
                sql: "\"Origin\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_RecruitmentApplicability",
                table: "Projects",
                sql: "(\"Origin\" = 0 AND \"MemberTarget\" IS NOT NULL AND \"MinimumQuantity\" IS NOT NULL AND \"RecruitmentDeadline\" IS NOT NULL) OR (\"Origin\" = 1 AND \"MemberTarget\" IS NULL AND \"MinimumQuantity\" IS NULL AND \"RecruitmentDeadline\" IS NULL AND \"RecruitmentStatus\" = 2 AND \"Status\" = 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalProjectDrafts_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ExternalProjectDrafts",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceDocuments_ProjectId_UploadedFileId",
                table: "ProjectEvidenceDocuments",
                columns: new[] { "ProjectId", "UploadedFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvidenceDocuments_UploadedFileId",
                table: "ProjectEvidenceDocuments",
                column: "UploadedFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Projects" WHERE "Origin" = 1) THEN
                        RAISE EXCEPTION 'External projects exist. Export or migrate them before rolling back this schema.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ProjectEvidenceDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Origin",
                table: "Projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_RecruitmentApplicability",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ExternalProjectDrafts_Status_NextAttemptAt_LeaseExpiresAt",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "ExternalDetailsJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ExternalLeaderName",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ImplementationEndDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ImplementationStartDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SharingRightsConfirmedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SubmitterContribution",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SubmitterIsLeader",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SubmitterRole",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "ExternalProjectDrafts");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ExternalProjectDrafts");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RecruitmentDeadline",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MinimumQuantity",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MemberTarget",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
