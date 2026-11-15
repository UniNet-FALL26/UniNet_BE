# UniNet_FE — Gỡ giao diện L1 và cập nhật hoàn thành dự án

Ngày cập nhật: 09/11/2026.

Tài liệu bàn giao cho UniNet_FE, đối chiếu source UniNet_BE hiện tại. Phạm vi triển khai là frontend; không chạy migration database theo tài liệu này.

## 1. Kết quả cần đạt

- Dự án Completed không còn tab Minh chứng (Evidence) của luồng verification Level 1.
- Giao diện assessor không còn tab Minh chứng và các bước đánh giá L1.
- Dự án Active chỉ cho leader/người tạo dự án bấm Hoàn thành dự án.
- Bấm nút mở modal xác nhận trước; chỉ khi xác nhận mới gửi `PATCH /api/projects/status`.
- Luồng xác minh có phí, thanh toán, admin phân công assessor, meeting và report đánh giá vẫn hoạt động độc lập với L1.

## 2. Những thay đổi backend FE phải thích ứng

### 2.1. Level 1 đã bị gỡ khỏi runtime

- Không còn điều kiện phải có L1 Passed/current để tạo yêu cầu xác minh, đặt lịch, thanh toán hay phân công assessor.
- Không đọc hoặc yêu cầu `evidenceModeration` trong chi tiết verification/assignment.
- Không đọc `evidenceModerationId` từ kết quả xác nhận external project draft.
- Không gọi các endpoint đã gỡ:
  - `GET /api/projects/{projectId}/evidence-moderation`
  - `POST /api/projects/{projectId}/evidence-moderation/retry`
- Các route trên trả 404; không polling, retry hoặc hiển thị lỗi như một bước bắt buộc của quy trình hiện tại.
- Entity `ProjectVerificationEvidence`, DbSet và navigation `EvidenceSubmissions` đã bị gỡ khỏi model. Migration archive rồi xóa bảng đã chuẩn bị nhưng **chưa áp dụng lên database ứng dụng**.
- Không truy cập trực tiếp archive và không tạo màn hình evidence lịch sử mới trong phạm vi này.

### 2.2. Những chức năng vẫn giữ

- Xác minh project có phí: chọn gói, các điều kiện lịch/booking hiện tại, thanh toán, phân công assessor, meeting và assessment report.
- Trạng thái Completed, quyền sở hữu, tính hợp lệ của gói và thanh toán vẫn được backend kiểm tra.
- Kiểm duyệt nội dung khi tạo dự án, bao gồm nội dung/từ cấm, vẫn giữ nguyên.
- Xác minh tài khoản, đánh giá đóng góp và thông báo chung vẫn giữ nguyên.
- Không xóa tùy tiện mọi thành phần có tên `evidence`: tài liệu external project, file đính kèm hoặc dữ liệu report có thể phục vụ chức năng còn trong scope. Chỉ loại bỏ phần thuộc luồng L1 đã nghỉ dùng.

## 3. Thay đổi UI — Student/project workspace

1. Xóa tab Minh chứng/Evidence thuộc L1 khỏi cấu hình tab, menu, mobile navigation và các biến thể trang chi tiết dự án Completed.
2. Gỡ panel kết quả AI L1, badge Passed/NeedsEvidence/Rejected/Error L1, nút chạy lại, submit evidence L1 và các hướng dẫn bắt buộc hoàn thành L1.
3. Gỡ điều kiện vô hiệu hóa nút Request verification/Verify project dựa trên L1. Giữ các điều kiện hợp lệ khác từ backend.
4. Gỡ fetch/polling/cache invalidation phục vụ riêng L1, thay vì chỉ ẩn phần render nhưng vẫn gọi API.
5. Với URL/tab state cũ trỏ đến evidence L1, chuyển sang tab mặc định còn hợp lệ (ví dụ Overview), đồng thời chuẩn hóa URL. Áp dụng cả khi mở trực tiếp hoặc tải lại trang.
6. Nếu tab L1 được dùng chung giữa nhiều trạng thái dự án, loại bỏ khỏi cấu hình chung để không còn xuất hiện ở trạng thái khác.
7. Không tự động chuyển mọi thông báo lịch sử đến trang evidence đã gỡ. Dùng cơ chế giải quyết target hiện có; target không còn khả dụng hiển thị trạng thái phù hợp.

## 4. Thay đổi UI — Assessor

- Gỡ tab Minh chứng/Evidence L1, panel kết quả moderation L1, nút retry/review L1 và mọi L1 prerequisite trong assignment detail/project detail.
- Không đợi `evidenceModeration` hoặc `EvidenceSubmissions` trước khi render màn hình.
- Giữ thông tin project, assignment, lịch khả dụng, lịch/meeting và report đánh giá theo API hiện tại.
- Không vô hiệu hóa tham gia meeting, soạn hoặc nộp report chỉ vì thiếu dữ liệu L1.
- Report assessment sau meeting là chức năng khác với evidence moderation L1; không xóa form report, kết quả assessment hay dữ liệu rubric.
- Nếu có deep link vào evidence L1, chuyển sang tab mặc định phù hợp của assignment/project.
- Không gỡ permission key `verification.evidence.review` chỉ vì tên chứa evidence: backend hiện vẫn dùng key này trong quyền report/assignment.

## 5. Quyền bấm Hoàn thành dự án

Backend hiện thực thi leader bằng **project creator**, không phải chuỗi role tự nhập trong danh sách thành viên.

`ProjectDetailResponse` có `isCreator`, `isMember`, `creator` và có thể có `leader`. Dùng `isCreator` từ chi tiết dự án đã tải theo tài khoản đăng nhập làm điều kiện quyền. Không dùng `isMember` hoặc chỉ kiểm tra role Student để cho phép hoàn thành.

Điều kiện hiển thị và cho phép nút:

```ts
const canComplete =
  isAuthenticated &&
  currentAccount.role === 'Student' &&
  project.status === ProjectStatus.Active &&
  project.isCreator === true;
```

Chuẩn hóa enum theo client hiện có; backend dùng `Pending = 0`, `Active = 1`, `Completed = 2`, `Rejected = 3`. Không copy giá trị role trong ví dụ nếu client dùng enum khác.

| Người xem / trạng thái | Hành vi |
| --- | --- |
| Student creator, project Active | Hiện và cho phép bấm nút |
| Thành viên khác, dù project Active | Ẩn nút; không gọi API |
| Assessor/Admin/người ngoài/khách | Không có thao tác hoàn thành trong UI này |
| Project Pending/Completed/Rejected | Không hiện nút hoàn thành |
| Đang tải quyền/trạng thái hoặc đang gửi request | Không cho thao tác |

Lưu ý ID: `creator.id` là profile/user ID; token `sub` là account ID. Không so sánh hai loại ID này trực tiếp. Nếu dữ liệu danh sách chưa có `isCreator`, tải chi tiết để xác định quyền. Không suy luận quyền từ nhãn Leader, `leader` hoặc `members[].role` khi chúng không khớp creator.

## 6. Modal xác nhận và request

### 6.1. Trình tự

1. Leader bấm **Complete project**: chỉ mở modal, chưa gửi API.
2. Modal có tiêu đề **Complete project?**, nội dung **This will mark the project as completed and open contribution evaluations. Do you want to continue?**; nút **Cancel** và **Confirm completion**. Có thể dùng bản dịch tương ứng theo i18n hiện có.
3. Cancel/đóng modal trước khi xác nhận: không gửi request, giữ nguyên trạng thái.
4. Xác nhận: kiểm tra lại `canComplete`, gửi đúng một request, khóa nút xác nhận và hiển thị loading. Ngăn double click; khi đang gửi không cho đóng modal gây hiểu nhầm request đã bị hủy.
5. Chỉ khi thành công mới cập nhật UI sang Completed và đóng modal. Không đổi trạng thái vĩnh viễn trước khi nhận response.

### 6.2. API contract

```http
PATCH /api/projects/status
Authorization: Bearer <access_token>
Content-Type: application/json
```

```json
{
  "projectId": "<project-guid>",
  "status": 2
}
```

Đây là endpoint nhận project ID trong body; không thay bằng `/api/projects/{id}/status`. Backend chỉ hỗ trợ chuyển **Active → Completed** qua endpoint này.

Response thành công HTTP 200:

```json
{
  "projectId": "<project-guid>",
  "status": 2,
  "updatedAt": "2026-11-09T08:00:00Z",
  "completedAt": "2026-11-09T08:00:00Z"
}
```

Thời gian trên chỉ là ví dụ; dùng giá trị server trả về. Backend đồng thời tạo contribution cycle và gửi thông báo đến thành viên; FE không tạo cycle hoặc gửi thông báo trùng.

### 6.3. Sau khi thành công

- Hiển thị thông báo **Project completed successfully.**
- Cập nhật/invalidate project detail, danh sách My projects và các cache trạng thái/đánh giá đóng góp liên quan đang được FE sử dụng.
- Ẩn nút Complete project và render UI Completed không có tab evidence L1.
- Nếu đang đứng trên tab đã bị gỡ, chuyển về tab hợp lệ.
- Refresh dữ liệu để cập nhật các hành động theo trạng thái mới; không tự động thanh toán, tạo verification request hoặc điều hướng qua L1.

### 6.4. Xử lý lỗi

Lỗi nghiệp vụ trả `{ "code": "...", "message": "..." }`. Lỗi xác thực có thể không theo cấu trúc này; dùng HTTP status làm fallback.

| HTTP / code | Xử lý FE |
| --- | --- |
| 401 | Xử lý phiên đăng nhập theo cơ chế hiện có |
| 403 / FORBIDDEN | Thông báo không có quyền; refresh quyền và ẩn nút nếu không còn hợp lệ |
| 404 / PROJECT_NOT_FOUND | Thông báo dự án không tồn tại hoặc không còn khả dụng |
| 400 / INVALID_STATUS_TRANSITION | Không đổi local state; thông báo và tải lại dữ liệu |
| 409 / INVALID_STATUS_TRANSITION | Dự án không còn Active; refresh và render trạng thái server |
| 409 / PROJECT_CONFLICT | Có thay đổi đồng thời; refresh trước khi cho thử lại |
| Network error / 5xx | Hiển thị lỗi và mở lại khả năng thao tác sau khi kết thúc loading; khi không rõ request đã thành công chưa, tải lại trạng thái trước khi gửi lại |

Không tự động retry PATCH vô hạn hoặc coi 409 là thành công mà chưa tải lại project. Với mọi lỗi, không để loading bị kẹt và không giả lập Completed.

## 7. Checklist nghiệm thu

- [ ] Dự án Completed không hiện tab Minh chứng trên desktop/mobile, kể cả reload và deep link cũ.
- [ ] Assessor không còn giao diện hoặc API call L1, nhưng meeting/report vẫn dùng được.
- [ ] Không còn request tới hai endpoint evidence-moderation đã gỡ, kể cả polling/background fetch.
- [ ] Dự án Completed không có evidence record vẫn đi tiếp được luồng verification có phí, theo các điều kiện còn lại.
- [ ] Chỉ Student creator của dự án Active thấy và bấm được Complete project; thành viên khác không có thao tác này.
- [ ] Bấm nút chỉ mở modal; Cancel không tạo request.
- [ ] Confirm gửi đúng PATCH/body; double click chỉ tạo một request.
- [ ] Thành công cập nhật trạng thái, ẩn nút, cập nhật cache và không hiện lại tab evidence.
- [ ] Có kiểm tra 403, 404, 409, network error và request timeout; không có trạng thái hoàn thành giả.
- [ ] Không làm mất community notifications, contribution evaluations, kiểm duyệt nội dung hoặc xác minh tài khoản.

## 8. Nguồn backend đã đối chiếu

- `UniNet.API/Controllers/ProjectsController.cs` — route, role và error response.
- `UniNet.Application/Services/ProjectService.cs` — creator-only, Active → Completed, contribution cycle và notification.
- `UniNet.Application/DTOs/Projects/ProjectStatusContracts.cs` — request/response.
- `UniNet.Application/DTOs/Projects/ProjectContracts.cs` — project detail và `isCreator`.
- `UniNet.Domain/Enums/ProjectStatus.cs` — giá trị trạng thái.
- `docs/handoffs/remove-verification-l1-prerequisite.md` — L1 runtime/API đã gỡ.
- `docs/operations/archive-remove-verification-evidence.md` — migration evidence archive, chưa áp dụng database ứng dụng.
- `docs/reviews/verification-notification-usage.md` — thông báo chung cần giữ.
