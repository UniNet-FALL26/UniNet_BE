# Project refactor — TASK 1: loại bỏ 11 bảng cũ

Cập nhật: 16/11/2026. Người dùng xác nhận database trong `.env` là development và yêu cầu áp dụng migration.

## Kết quả thực tế

Đã áp dụng trên database development:

1. `20261116023634_PrepareSimplifiedProjectSchema`.
2. `20261116023712_BackfillSimplifiedProjectSchema`.
3. `20261116030240_ArchiveAndRemoveLegacyProjectTables`.

Đối chiếu trực tiếp sau migration:

- Số bảng cũ còn trong schema hoạt động: **0/11**.
- Sáu bảng nghiệp vụ mới đều tồn tại.
- Kho lưu trữ có đủ 11 bảng nguồn, **89 bản ghi**, 11/11 checksum hợp lệ.
- Số bản ghi của Accounts, Users, Skills, Specializations, UploadedFiles, CareerProfiles,
  Notifications, ContributionCycles, ContributionResults, ProjectVerifications, Transactions
  không đổi so với trước migration.
- Không reset database, không sửa/xóa migration lịch sử đã áp dụng.

## Các bảng đã xóa

`ProjectRoleRequirements`, `ProjectSkills`, `ProjectInvitations`, `WorkspaceChannels`,
`WorkspaceMessages`, `ProjectTasks`, `ProjectTaskAssignments`, `TaskReports`,
`TaskEvidence`, `TaskReportReviews`, `ExternalProjectDrafts`.

Đã xóa entity, DbSet, navigation và Fluent API tương ứng. `ProjectPosition` và
`ProjectTechnology` là các value object JSON có NotMapped, không phải bảng thay thế.

## Chuyển dữ liệu và lưu trữ

- Vị trí tuyển sang ProjectRecruitments.Positions, giữ UUID ổn định và thông tin mở rộng.
- Kỹ năng sang Projects.Technologies; giữ skillId/name/category/iconUrl để các API cũ
  còn đọc được danh mục. Reader hỗ trợ cả tên dạng chuỗi và object JSON.
- Lời mời sang ProjectJoinRequests với RequestType = Invite; giữ ID, người mời/nhận,
  nội dung, lịch sử và năm trạng thái. Kiểm tra xung đột danh tính trước khi drop.
- Đơn Apply và Invite được lọc riêng; chấp nhận đơn lưu JoinRequestId vào thành viên.
- Description, ngày hợp lệ và CompletedAt được sao chép sang ProjectDetails;
  ngày không hợp lệ vẫn còn ở cột nguồn để đối soát.
- Toàn bộ dòng/cột gốc của 11 bảng được lưu vào
  `project_legacy_archive_<md5(source_schema)>.Snapshots` trước khi drop.
  Kho kỹ thuật này lưu TableName, SourceSchema, RowCount, Data, Checksum, có trigger
  cấm INSERT/UPDATE/DELETE/TRUNCATE và thu hồi quyền PUBLIC trên schema.
- Khóa ACCESS EXCLUSIVE các bảng nguồn trong giao dịch drop để chặn ghi muộn.
  Lỗi định danh hoặc FK chưa xử lý khiến giao dịch dừng, không dùng DROP CASCADE.
- Down chủ động từ chối: khôi phục cần quy trình đối soát từ archive, không tạo lại
  bảng rỗng hoặc làm mất các thay đổi mới trên schema thay thế.

## Phần code phụ thuộc đã xử lý

- ProjectService, ProjectRoleCapacity và ProjectMemberService đọc/ghi JSON vị trí/kỹ năng.
- ProjectInvitationService dùng bảng yêu cầu chung nhưng giữ hợp đồng API lời mời cũ.
- Portfolio, xác minh, phân công xác minh và kiểm duyệt đọc thông tin công nghệ đã chuyển.
- Thông báo lời mời cũ vẫn mở được bằng cùng ID; thông báo Task không còn đích hoạt động.
- Task, báo cáo công việc, kênh Workspace và nháp bên ngoài ngừng hỗ trợ;
  các phương thức cũ trả HTTP 410, mã PROJECT_FEATURE_RETIRED, không truy vấn bảng đã xóa.
- Bỏ worker hết hạn nháp. Không đổi các worker độc lập khác.
- ContributionService giữ đọc kết quả lịch sử; tính lại dựa trên Task trả 410 với mã
  CONTRIBUTION_INPUTS_RETIRED. Không sinh điểm giả từ tập công việc trống.
- Không sửa frontend, không triển khai các API mới của TASK 2.

## Kiểm tra

- Backend và project test build được.
- Kiểm tra tập trung: 45/45 qua, gồm PostgreSQL migration và kiểm tra HTTP 410 thực tế.
- Sau bổ sung kiểm tra JoinRequestId: 44/44 kiểm tra không cần PostgreSQL qua.
- PostgreSQL fixture chạy toàn bộ lịch sử migration trên database ngẫu nhiên riêng,
  seed đủ 11 bảng, kiểm tra dữ liệu archive khớp nguồn, 11 bảng biến mất, UQ/FK/model,
  lỗi xung đột làm rollback nguyên tử, archive chống ghi và migration không chạy lặp.
- Hồi quy toàn backend không gồm Postgres: 635 test, 631 qua, 4 lỗi có sẵn.
  Chạy lại trên bản HEAD riêng tái hiện đủ bốn lỗi:
  - DatabaseDesignTests.SkillsUseExactlyFourCategories: kỳ vọng check constraint cũ.
  - Hai trường hợp AuthTests.InvalidPartnerNamesDoNotCreateAnAccount.
  - AuthTests.PartnerRegistrationSeparatesOrganizationAndPersonalNames.
- Đã sửa fixture Portfolio thiếu MemberTarget để kiểm tra đúng constraint hiện có;
  không sửa nghiệp vụ Auth hoặc danh mục Skills ngoài phạm vi.
- EF xác nhận không có model change chưa được ghi migration; đã sinh SQL drop để rà soát.

## Phần còn lại ngoài yêu cầu xóa bảng

- Giữ enum ProjectStatus cũ theo xác nhận trước của người dùng; chưa ánh xạ Pending/Rejected.
- Các cột Projects cũ còn được reader/writer sử dụng, chưa loại bỏ trong lần xóa 11 bảng này.
- TASK 2: API tạo/chỉnh sửa/mở cộng đồng mới và kết nối chat thay thế cần xác nhận riêng.
- TASK 3: frontend cần bỏ các luồng Task/Workspace/nháp đã trả 410.
- Nếu backend đang chạy bản build cũ, cần khởi động lại bằng code mới để dùng schema mới.