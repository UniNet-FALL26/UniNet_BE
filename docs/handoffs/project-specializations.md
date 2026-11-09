# Chuyên ngành dự án và gợi ý assessor

Đối chiếu triển khai backend: 09/11/2026. ProjectField giữ nguyên ý nghĩa và contract hiện có. Specialization là danh mục riêng; không suy luận chuyên ngành từ ProjectField hoặc AssessorJson.Expertise dạng văn bản.

## Mô hình

- Specializations: id UUID, code chuẩn hóa uppercase (1–100 ký tự A–Z, 0–9, _), name 1–255 ký tự sau trim, isActive.
- Projects.SpecializationId: FK nullable để giữ được dự án cũ; dự án tạo mới phải chọn một chuyên ngành active.
- AssessorSpecializations: quan hệ nhiều chuyên ngành cho một assessor, khóa kép (assessorUserId, specializationId). Admin quản lý bằng API riêng, không sửa AssessorJson.Expertise và không gộp chuyên ngành với kỹ năng.
- Ba chuyên ngành khởi tạo: SOFTWARE_ENGINEERING (Kỹ thuật phần mềm), ARTIFICIAL_INTELLIGENCE (Trí tuệ nhân tạo), INFORMATION_SECURITY (An toàn thông tin).
- Không tự gán ngành cho dữ liệu cũ. Assessor chưa được gán ngành sẽ không có trong kết quả khớp ngành.

## API danh mục

GET /api/specializations: công khai, trả mảng chuyên ngành active, sắp theo tên rồi ID.

GET /api/admin/specializations: Admin Active có staff.manage, trả cả active/inactive.

POST /api/admin/specializations: Admin Active có staff.manage, trả 201. PUT /api/admin/specializations/{id}: cùng quyền, trả 200. Cùng body:

```json
{ "code": "SOFTWARE_ENGINEERING", "name": "Kỹ thuật phần mềm", "isActive": true }
```

Response:
```json
{ "id": "a4900000-0000-0000-0000-000000000001", "code": "SOFTWARE_ENGINEERING", "name": "Kỹ thuật phần mềm", "isActive": true }
```

Code duy nhất; trùng trả 409 SPECIALIZATION_CODE_EXISTS. PUT isActive=false để ngừng sử dụng, không xóa lịch sử dự án/assessor. Các response dự án vẫn hiện chuyên ngành inactive đã gán trước đó.

## Request tạo/sửa dự án

POST /api/projects: giữ các trường cũ, thêm specializationId bắt buộc ở tầng nghiệp vụ (UUID chuyên ngành active). Ví dụ body rút gọn, vẫn cần gửi đầy đủ trường tạo dự án hiện có:

```json
{
  "title": "Ứng dụng quản lý nhóm",
  "projectField": "Công nghệ thông tin",
  "specializationId": "a4900000-0000-0000-0000-000000000001"
}
```

Thiếu hoặc UUID rỗng: 400 SPECIALIZATION_REQUIRED; ID không có/inactive: 400 INVALID_SPECIALIZATION. Đây là thay đổi yêu cầu đầu vào: FE phải tải catalog và thêm bộ chọn trước khi phát hành cùng backend.

UpdateProjectRequest cũng có specializationId tùy chọn; bỏ/null nghĩa giữ nguyên, không xóa ngành. Áp dụng quyền và trạng thái sửa dự án hiện có. Không được đổi chuyên ngành sau khi đã có yêu cầu thẩm định (409 SPECIALIZATION_LOCKED).

External project: PATCH draft gửi data.specializationId. Bản nháp thiếu trường này có missingFields chứa specializationId, vẫn lưu nháp được; nếu gửi ID thì phải tồn tại và active. Khi confirm tạo project, chuyên ngành bắt buộc; lưu vào Projects.SpecializationId. AI không tự đoán mã ngành, người dùng chọn từ catalog. Bản nháp cũ chưa confirm cần bổ sung trường này; confirm lại một draft đã hoàn tất vẫn trả kết quả cũ theo cơ chế idempotent.

## Response dự án

ProjectDetailResponse, ProjectListItemResponse, DiscoveryProjectResponse, VerificationProjectResponse và AssignmentProjectResponse đều có trường mới:

```json
{
  "projectField": "Công nghệ thông tin",
  "specialization": {
    "id": "a4900000-0000-0000-0000-000000000001",
    "code": "SOFTWARE_ENGINEERING",
    "name": "Kỹ thuật phần mềm",
    "isActive": true
  }
}
```

Với dự án cũ chưa phân loại: specialization=null. Đọc ID cho form từ specialization.id. Bao gồm màn chi tiết dự án, danh sách của người dùng/theo trạng thái, discovery, chi tiết moderation sử dụng ProjectDetailResponse, chi tiết thẩm định của Admin/thành viên và chi tiết phân công của assessor.

## Gán ngành cho assessor

Admin Active có staff.manage:

- GET /api/admin/assessors/{userId}/specializations → mảng chuyên ngành đã gán, kể cả ngành bị tắt về sau.
- PUT /api/admin/assessors/{userId}/specializations → thay thế toàn bộ danh sách, trả mảng sau cập nhật.

```json
{
  "specializationIds": [
    "a4900000-0000-0000-0000-000000000001",
    "a4900000-0000-0000-0000-000000000002"
  ]
}
```

userId là ID hồ sơ trong Users, không phải Account.Id. ID hồ sơ không phải Assessor trả 404 ASSESSOR_NOT_FOUND. Tối đa 30 ID, không có UUID rỗng, mọi ngành phải active; loại trùng. Mảng rỗng xóa toàn bộ liên kết. Admin có thể chuẩn bị chuyên ngành cho assessor chưa Active nhưng chỉ người Active mới được gợi ý. PUT có giao dịch; tranh chấp ghi trả 409 SPECIALIZATION_CONFLICT. Không có quyền cập nhật ngành của người khác từ API profile công khai.

## Bổ sung ngành cho dự án cũ

PUT /api/admin/projects/{projectId}/specialization

```json
{ "specializationId": "a4900000-0000-0000-0000-000000000001" }
```

Admin Active có verification.evidence.review. Trả đối tượng chuyên ngành. Cho phép điền ngành còn thiếu cho dự án cũ kể cả đã có hồ sơ thẩm định. Một khi dự án đã có ngành và có bất kỳ hồ sơ thẩm định, không cho đổi sang ngành khác (409 SPECIALIZATION_LOCKED); gọi lại cùng ngành active được phép. Không thay đổi trạng thái dự án, lịch họp, thanh toán hoặc kết quả thẩm định. Dữ liệu externalDetails là bản khai đã nộp; trường specialization ở cấp project là nguồn hiện tại sau khi Admin bổ sung.

## API gợi ý assessor theo ngành

GET /api/admin/projects/{projectId}/suitable-assessors?page=1&pageSize=20

Quyền: Admin Active có verification.evidence.review. page>=1; pageSize 1–100. Không yêu cầu dự án đã thanh toán, Completed hoặc đã có lịch để Admin có thể tìm người từ trước.

```json
{
  "projectId": "<uuid>",
  "specialization": { "id": "<uuid>", "code": "SOFTWARE_ENGINEERING", "name": "Kỹ thuật phần mềm", "isActive": true },
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1,
  "assessors": [
    {
      "userId": "<profile-uuid-dung-khi-assign>",
      "accountId": "<account-uuid>",
      "fullName": "Nguyễn Văn A",
      "avatarUrl": null,
      "jobTitle": "Senior Engineer",
      "yearsOfExperience": 8,
      "specialization": { "id": "<uuid>", "code": "SOFTWARE_ENGINEERING", "name": "Kỹ thuật phần mềm", "isActive": true }
    }
  ]
}
```

Chỉ trả account Assessor Active, có quyền verification.evidence.review hiệu lực và được gán đúng ngành bằng ID. Loại người tạo dự án và thành viên active còn trong nhóm để tránh gợi ý tự thẩm định dự án mình. Mỗi assessor xuất hiện một lần dù có nhiều chuyên ngành. Sắp theo fullName rồi userId, phân trang ở database. Không khớp chuỗi tự do ProjectField/Expertise, không tự chuyển sang ngành khác khi không có ứng viên.

Danh sách này chỉ xác định khớp chuyên môn. Khi phân công hoặc thay người theo lịch cụ thể, tiếp tục dùng GET /api/assignments/availability?projectVerificationId=... (thêm replaceAssignmentId khi thay người). Dashboard đối chiếu userId từ cả hai danh sách, rồi gọi API tạo/thay phân công hiện có; backend vẫn kiểm tra quyền, booking/thanh toán, lịch rảnh, xung đột và lifecycle như trước. API mới không tự phân công và không thay đổi luật assignment hiện có để bắt buộc chuyên ngành.

Lỗi: 404 PROJECT_NOT_FOUND; 409 SPECIALIZATION_REQUIRED nếu dự án cũ chưa có ngành; 409 SPECIALIZATION_INACTIVE nếu ngành đang tắt; 400 INVALID_PAGINATION; 403 FORBIDDEN nếu thiếu quyền; 200 assessors=[] nếu có ngành active nhưng chưa có người khớp. Không biến tất cả lỗi thành danh sách rỗng.

## Triển khai database

Migration: 20261109090826_AddProjectSpecializations, có FK, index và seed ba ngành. Giữ ProjectField và mọi dữ liệu dự án cũ; SpecializationId của dự án cũ để null. Không tự phân loại lịch sử theo chuỗi gần giống.

```powershell
dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.API
```

Migration cần được áp dụng lên database đích trước khi chạy phiên bản API mới. Việc tạo mã migration không đồng nghĩa đã cập nhật database đang vận hành. Không đưa chuỗi kết nối/bí mật vào tài liệu hoặc log.

## Checklist tích hợp

1. Áp dụng migration, kiểm tra GET catalog có ba ngành khởi tạo.
2. Dashboard gán ngành cho assessor; FE bổ sung selector khi tạo internal và hoàn thiện external draft.
3. Các màn GET project hiển thị ngành từ specialization, xử lý null cho dữ liệu cũ, giữ ProjectField.
4. Admin bổ sung ngành còn thiếu, gọi suitable-assessors và lấy giao với availability khi phân công theo lịch.
5. Kiểm tra ngành không tồn tại/inactive, quyền Admin bị thu hồi, assessor Suspended/thiếu quyền/không cùng ngành/thành viên dự án, phân trang và cập nhật đồng thời.

## Kết quả kiểm tra trong lần triển khai này

- Build thành công. EF xác nhận không còn thay đổi model nằm ngoài migration.
- Nhóm kiểm thử liên quan dự án/thẩm định/external/recruitment đều đạt, bao gồm kiểm thử HTTP API chuyên ngành mới.
- Toàn bộ suite: 723 đạt, 4 thất bại, 44 bỏ qua. Bốn lỗi thuộc AuthTests (tên/nickname khi đăng ký Partner) và DatabaseDesignTests.SkillsUseExactlyFourCategories (assert cũ không chấp nhận Category nullable); mã xử lý tương ứng không được thay đổi trong tính năng này.
- 44 kiểm thử PostgreSQL bị bỏ qua vì chưa cấu hình UNINET_TEST_POSTGRES. Chưa chạy migration lên database đích.
