# Các bảng dữ liệu liên quan đến dự án

Cập nhật 16/11/2026, sau TASK 2 và migration `20261116034415_AllowPersonalProjectLifecycle`, đã áp dụng trên database development được xác nhận.

Module có **6 bảng nghiệp vụ**; tài liệu bổ sung 3 bảng tài nguyên/danh mục dùng chung. Không liệt kê các bảng xác minh, thanh toán, kiểm duyệt và đánh giá đóng góp.

PK: khóa chính; FK: khóa ngoại; UQ: duy nhất. Kiểu theo PostgreSQL. Users.Id là hồ sơ, Accounts.Id là tài khoản đăng nhập. JSON không phải bảng riêng.


## 1. Projects — Thông tin dự án

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK, mã dự án |
| LogoUrl | text | Có | URL ảnh/logo dự án |
| Technologies | jsonb | Có | Danh sách công nghệ dạng JSON; hỗ trợ tên và giá trị danh mục cũ |
| ChatRoomId | uuid | Có | Mã phòng trò chuyện tùy chọn; chưa có FK hoặc chức năng chat dự án |
| CreatorId | uuid | Không | FK → Users.Id, người tạo và quản lý bản ghi dự án |
| Origin | smallint | Không | Nguồn dự án: 0 = Internal, 1 = External; mặc định DB là Internal |
| SubmitterIsLeader | boolean | Không | Người gửi có phải trưởng nhóm thực tế hay không |
| ExternalLeaderName | varchar(255) | Có | Tên trưởng nhóm dự án bên ngoài |
| ImplementationStartDate | varchar(10) | Có | Ngày bắt đầu triển khai, lưu dạng chuỗi |
| ImplementationEndDate | varchar(10) | Có | Ngày kết thúc triển khai, lưu dạng chuỗi |
| SubmitterRole | varchar(255) | Có | Vai trò của người gửi trong dự án bên ngoài |
| SubmitterContribution | varchar(4000) | Có | Mô tả đóng góp của người gửi |
| ExternalDetailsJson | jsonb | Có | Thông tin mở rộng của dự án bên ngoài |
| SharingRightsConfirmedAt | timestamptz | Có | Thời điểm xác nhận quyền chia sẻ thông tin |
| Title | varchar(255) | Không | Tên dự án |
| ProjectField | varchar(150) | Không | Lĩnh vực dự án |
| SpecializationId | uuid | Có | FK → Specializations.Id, chuyên ngành |
| Description | text | Không | Mô tả dự án |
| MemberTarget | integer | Có | Số thành viên mục tiêu cũ; tùy chọn |
| MinimumQuantity | integer | Có | Số thành viên tối thiểu cũ; tùy chọn |
| RecruitmentDeadline | timestamptz | Có | Hạn tuyển cũ; tùy chọn, API đồng bộ khi lưu tuyển dụng |
| ExpectedOutput | timestamptz | Có | Mốc thời gian đầu ra dự kiến; model hiện lưu thời gian, không phải mô tả sản phẩm |
| Visibility | smallint | Không | 0 = Private, 1 = Public |
| Status | smallint | Không | 0 = Pending, 1 = Active, 2 = Completed, 3 = Rejected |
| RecruitmentStatus | smallint | Không | 0 = Open, 1 = Full, 2 = Closed, 3 = Expired |
| CreatedAt | timestamptz | Không | Thời điểm tạo |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |
| CompletedAt | timestamptz | Có | Thời điểm hoàn thành |

Các cột tuyển dụng cũ được giữ để tương thích. Migration AllowPersonalProjectLifecycle đã bỏ CK_Projects_RecruitmentApplicability: dự án cá nhân không bắt buộc tuyển thành viên; dự án bên ngoài có thể đang thực hiện. API mới mặc định External, riêng tư; tên tối đa 200, mô tả ngắn tối đa 500 ký tự. Enum trạng thái cũ được giữ nguyên theo yêu cầu; không đổi Rejected thành Paused.


## 2. ProjectDetails — Chi tiết dự án

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| ProjectId | uuid | Không | Khóa ngoại tới Projects.Id |
| Description | text | Có | Nội dung mô tả |
| Features | jsonb | Có | Danh sách chức năng dạng JSON |
| Results | jsonb | Có | Vấn đề, giải pháp, mục tiêu, kết quả đạt được hoặc mong đợi dạng JSON |
| Documents | jsonb | Có | Danh sách tài liệu dạng JSON |
| Media | jsonb | Có | Danh sách hình ảnh và video dạng JSON |
| Links | jsonb | Có | Danh sách liên kết dạng JSON |
| StartDate | date | Có | Ngày bắt đầu |
| EndDate | date | Có | Ngày kết thúc hoặc dự kiến kết thúc |
| CompletedAt | timestamptz | Có | Thời điểm hoàn thành |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |

Quan hệ 1–0..1, ProjectId là PK và FK Cascade tới Projects.Id. Không lưu tổng kết cá nhân tại đây.


## 3. ProjectRecruitments — Tuyển thành viên

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK |
| ProjectId | uuid | Không | Khóa ngoại tới Projects.Id |
| Description | text | Có | Nội dung mô tả |
| Positions | jsonb | Không | Các vị trí dạng JSON: ID ổn định, tên, số lượng, công nghệ, kỹ năng, mô tả, yêu cầu, quyền lợi |
| Deadline | timestamptz | Có | Hạn tuyển; tùy chọn |
| Status | smallint | Không | 0 = Draft, 1 = Open, 2 = Closed |
| PublishedAt | timestamptz | Có | Thời điểm đăng tuyển |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |

ProjectId duy nhất, FK Cascade tới Projects.Id. Không có bảng vị trí riêng. ID vị trí là chuỗi ổn định, tương thích UUID cũ. Mở tuyển là thao tác rõ ràng; không tự tạo khi tạo dự án.


## 4. ProjectJoinRequests — Ứng tuyển và lời mời

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK |
| RequestType | smallint | Không | 0 = Apply, 1 = Invite; mặc định Apply |
| InviterId | uuid | Có | FK tới Users.Id, người mời |
| PositionId | varchar(100) | Có | ID vị trí trong ProjectRecruitments.Positions; chuỗi tối đa 100 ký tự |
| ReviewedBy | uuid | Có | FK tới Users.Id, người xử lý |
| ReviewedAt | timestamptz | Có | Thời điểm xử lý |
| ProjectId | uuid | Không | FK → Projects.Id |
| UserId | uuid | Không | FK → Users.Id, người yêu cầu |
| Role | varchar(150) | Không | Vai trò muốn tham gia |
| Message | text | Có | Lời nhắn ứng tuyển |
| Status | smallint | Không | Enum: 0 = Pending, 1 = Accepted, 2 = Rejected, 3 = Cancelled, 4 = Expired; xem lưu ý bên dưới |
| CreatedAt | timestamptz | Không | Thời điểm gửi |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |

Index (ProjectId, Status), (UserId, Status). Status hợp lệ 0–4; RequestType 0–1. Chấp nhận yêu cầu vẫn giữ lịch sử; thành viên liên kết qua JoinRequestId. FK người dùng dùng Restrict, dự án dùng Cascade.


## 5. ProjectMembers — Thành viên

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK |
| JoinRequestId | uuid | Có | FK Restrict tới ProjectJoinRequests.Id; giữ lịch sử yêu cầu đã chấp nhận |
| PositionId | varchar(100) | Có | ID vị trí trong ProjectRecruitments.Positions; chuỗi tối đa 100 ký tự |
| ProjectId | uuid | Không | FK → Projects.Id |
| UserId | uuid | Không | FK → Users.Id |
| Role | varchar(150) | Không | Vai trò trong dự án |
| Status | smallint | Không | 0 = Active, 1 = Kicked, 2 = Left |
| JoinedAt | timestamptz | Không | Thời điểm tham gia |
| LeftAt | timestamptz | Có | Thời điểm rời dự án |
| CreatedAt | timestamptz | Không | Thời điểm tạo bản ghi |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |

UQ (ProjectId, UserId). Index (ProjectId, Status), (UserId, Status). FK dự án Cascade, người dùng Restrict. Giữ lịch sử khi rời hoặc bị xóa khỏi nhóm.


## 6. ProjectPersonalReviews — Tổng kết cá nhân

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK |
| ProjectId | uuid | Không | Khóa ngoại tới Projects.Id |
| UserId | uuid | Không | Khóa ngoại tới Users.Id |
| Challenges | text | Có | Khó khăn cá nhân |
| LessonsLearned | text | Có | Bài học kinh nghiệm cá nhân |
| CreatedAt | timestamptz | Không | Thời điểm tạo |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |

UQ (ProjectId, UserId). FK dự án Cascade, người dùng Restrict. API chỉ cho người dùng sửa tổng kết của chính mình.


## 7. UploadedFiles — Tệp dùng chung

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK |
| OwnerAccountId | uuid | Không | FK → Accounts.Id, chủ tệp |
| PublicId | varchar(512) | Không | Mã tài nguyên trên dịch vụ lưu trữ |
| Url | text | Không | Đường dẫn tệp |
| FileName | varchar(255) | Không | Tên tệp |
| Format | varchar(20) | Không | Định dạng tệp |
| ResourceType | varchar(10) | Không | Chỉ nhận image, raw hoặc video |
| SizeBytes | bigint | Không | Kích thước tệp theo byte, phải lớn hơn 0 |
| CreatedAt | timestamptz | Không | Thời điểm tải lên |

UQ (ResourceType, PublicId). Index (OwnerAccountId, CreatedAt). FK chủ tệp Restrict. Tài nguyên dùng chung, không có FK trực tiếp tới Projects.


## 8. Skills — Danh mục kỹ năng

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK; danh mục dùng chung |
| Name | varchar(100) | Không | Tên kỹ năng |
| IconUrl | text | Có | Đường dẫn biểu tượng |
| Category | smallint | Có | 0 = Frontend, 1 = Backend, 2 = Design, 3 = ToolsAndOther |
| IsActive | boolean | Không | Kỹ năng còn được sử dụng |
| CreatedAt | timestamptz | Không | Thời điểm tạo |
| UpdatedAt | timestamptz | Không | Thời điểm cập nhật |

Index (Category, IsActive). Category cho phép NULL hoặc 0–3. ProjectSkills đã xóa; công nghệ hiện lưu JSON.


## 9. Specializations — Danh mục chuyên ngành

| Trường | Kiểu dữ liệu | NULL | Ý nghĩa / khóa |
|---|---|---|---|
| Id | uuid | Không | PK, được Projects.SpecializationId tham chiếu |
| Code | varchar(100) | Không | Mã chuyên ngành, UQ |
| Name | varchar(255) | Không | Tên chuyên ngành |
| IsActive | boolean | Không | Chuyên ngành còn được sử dụng |

Code duy nhất. Projects.SpecializationId tham chiếu với Restrict.


## Bảo toàn dữ liệu cũ

Đã xóa 11 bảng khỏi schema hoạt động: ProjectRoleRequirements, ProjectSkills,
ProjectInvitations, WorkspaceChannels, WorkspaceMessages, ProjectTasks,
ProjectTaskAssignments, TaskReports, TaskEvidence, TaskReportReviews, ExternalProjectDrafts.

Dữ liệu gốc được giữ trong schema kỹ thuật
`project_legacy_archive_<md5(source_schema)>.Snapshots`, có số dòng, checksum và
trigger chống ghi. Đây là kho lưu trữ, không thuộc 6 bảng nghiệp vụ và không được API sử dụng.
Portfolio tiếp tục giữ ProjectId trong CareerProfiles.ProjectsJson.

## Nguồn

- [Entities](../UniNet.Domain/Entities/)
- [UniNetDbContext](../UniNet.Infrastructure/Data/UniNetDbContext.cs)
- [Cấu hình schema](../UniNet.Infrastructure/Data/SimplifiedProjectConfiguration.cs)
- [Handoff TASK 2](handoffs/project-hub-task2.md)
