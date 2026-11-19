# Project refactor: TASK 1 — xóa 11 bảng cũ

## Phạm vi được xác nhận

Người dùng yêu cầu loại bỏ ProjectRoleRequirements, ProjectSkills, ProjectInvitations,
WorkspaceChannels, WorkspaceMessages, ProjectTasks, ProjectTaskAssignments,
TaskReports, TaskEvidence, TaskReportReviews, ExternalProjectDrafts.
Ngày 16/11/2026 người dùng xác nhận database `.env` là development và yêu cầu áp dụng migration.

Giữ sáu bảng nghiệp vụ mới: Projects, ProjectDetails, ProjectRecruitments,
ProjectJoinRequests, ProjectMembers, ProjectPersonalReviews; không tạo thêm bảng nghiệp vụ.
Kho lưu trữ kỹ thuật được tách khỏi model ứng dụng. Giữ các bảng dùng chung và migration lịch sử.

## Quyết định

- Chuyển vị trí/kỹ năng sang JSON và lời mời sang yêu cầu chung trước khi drop nguồn.
- Bảo toàn toàn bộ dữ liệu cũ trong archive có checksum và trigger chống ghi.
- Retire Task/Workspace/nháp bằng HTTP 410, không giữ DbSet chết hoặc truy vấn bảng đã xóa.
- Giữ đọc kết quả đóng góp lịch sử; chặn tính lại khi nguồn Task không còn.
- Giữ ProjectStatus cũ đến khi có quy tắc ánh xạ được xác nhận.
- Không đổi frontend hoặc triển khai mới toàn bộ API TASK 2.

## Các bước và tiêu chí kiểm chứng

1. Loại entity/mapping/navigation; chuyển các consumer còn dùng sang schema mới.
2. Thay kiểm tra hành vi đã bị loại bỏ bằng kiểm tra 410; giữ kiểm tra membership,
   Portfolio, đóng góp lịch sử và các module dùng chung.
3. Tạo migration archive/drop, không DROP CASCADE hoặc reset dữ liệu.
4. Thử PostgreSQL độc lập với dữ liệu đủ 11 bảng, so sánh archive, kiểm tra rollback
   khi xung đột và bảo đảm migration chỉ chạy một lần.
5. Build, kiểm tra model snapshot, chạy hồi quy, so sánh lỗi với HEAD.
6. Áp dụng development đã được xác nhận; kiểm tra trực tiếp 0 bảng cũ, 6 bảng mới,
   checksum và số dòng các bảng dùng chung trước/sau không đổi.

## Lệnh

Tại UniNet_BE:

```powershell
dotnet build UniNet.sln --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test UniNet.Tests --no-restore -m:1 --filter FullyQualifiedName~ProjectRefactor
dotnet ef migrations has-pending-model-changes --project UniNet.Infrastructure --startup-project UniNet.API --no-build
dotnet ef database update 20261116030240_ArchiveAndRemoveLegacyProjectTables --project UniNet.Infrastructure --startup-project UniNet.API --no-build
```

Không dùng lệnh cuối trên production. Handoff chứa kết quả chạy thực tế và giới hạn.