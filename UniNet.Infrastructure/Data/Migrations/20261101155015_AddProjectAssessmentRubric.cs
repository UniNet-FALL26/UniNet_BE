using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAssessmentRubric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_Status",
                table: "ProjectVerifications");

            migrationBuilder.AddColumn<string>(
                name: "AssessmentJson",
                table: "VerificationReports",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssessmentRubricJson",
                table: "ProjectVerifications",
                type: "jsonb",
                nullable: true);

            // Freeze the published 1.0 definition here; never read a mutable application policy.
            // Historical completed reports retain null: their scores cannot be reconstructed.
            migrationBuilder.Sql("""
                UPDATE "ProjectVerifications" v
                SET "AssessmentRubricJson" = '{
                  "Code":"UAS-SOFTWARE-PROJECT","Version":"1.0","PassingScore":65,
                  "Criteria":[
                    {"Code":"requirements","Name":"Vấn đề và yêu cầu","Weight":10},
                    {"Code":"architecture","Name":"Kiến trúc và thiết kế","Weight":15},
                    {"Code":"code_quality","Name":"Chất lượng mã nguồn","Weight":20},
                    {"Code":"functionality","Name":"Chức năng và hoàn thiện","Weight":20},
                    {"Code":"testing","Name":"Kiểm thử và độ tin cậy","Weight":15},
                    {"Code":"security","Name":"Bảo mật và dữ liệu","Weight":10},
                    {"Code":"documentation","Name":"Tài liệu và triển khai","Weight":5},
                    {"Code":"applicability","Name":"Tính ứng dụng","Weight":5}
                  ],
                  "Conditions":[
                    {"Code":"reviewable_evidence","Description":"Có demo, sản phẩm hoặc tài liệu đủ để kiểm tra và có quyền chia sẻ cho mục đích đánh giá"},
                    {"Code":"core_functions","Description":"Kiểm tra được các chức năng chính trong phạm vi công bố"},
                    {"Code":"no_serious_fabrication","Description":"Không có bằng chứng giả mạo nghiêm trọng"},
                    {"Code":"no_concealed_critical_vulnerabilities","Description":"Không che giấu lỗ hổng nghiêm trọng đã xác nhận trong phạm vi đánh giá"}
                  ]
                }'::jsonb
                WHERE v."Status" = 0 AND v."FinalizedAt" IS NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM "VerificationAssignments" a
                    JOIN "VerificationReports" r ON r."AssignmentId" = a."Id"
                    WHERE a."ProjectVerificationId" = v."Id");
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectVerifications_Status",
                table: "ProjectVerifications",
                sql: "\"Status\" BETWEEN 0 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "VerificationReports" WHERE "AssessmentJson" IS NOT NULL)
                     OR EXISTS (SELECT 1 FROM "ProjectVerifications" WHERE "Status" = 4) THEN
                    RAISE EXCEPTION 'Cannot downgrade: criterion-based assessment results must be preserved';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectVerifications_Status",
                table: "ProjectVerifications");

            migrationBuilder.DropColumn(
                name: "AssessmentJson",
                table: "VerificationReports");

            migrationBuilder.DropColumn(
                name: "AssessmentRubricJson",
                table: "ProjectVerifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectVerifications_Status",
                table: "ProjectVerifications",
                sql: "\"Status\" BETWEEN 0 AND 3");
        }
    }
}
