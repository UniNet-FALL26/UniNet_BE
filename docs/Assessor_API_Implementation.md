# Hướng dẫn triển khai tích hợp API dành cho Assessor

Ngày đối chiếu mã nguồn: 02/11/2026. Dự án: UniNet_BE.

## 1. Mục tiêu và phạm vi

Tích hợp các chức năng hiện có của thẩm định viên (Assessor, role = 2): công việc được phân công, lịch rảnh, họp thẩm định, báo cáo đánh giá, hồ sơ chuyên môn, ví thu nhập, thông báo và cộng đồng.

Các endpoint bên dưới đã có trong backend. Tài liệu là hướng dẫn triển khai client và kiểm tra tích hợp; không yêu cầu tạo lại API hoặc thêm endpoint chưa tồn tại. Phần checklist là công việc cần xác nhận khi triển khai, không phải kết quả kiểm thử đã chạy.

## 2. Quy ước chung

- Base URL lấy từ cấu hình môi trường; các đường dẫn bên dưới đã bao gồm `/api`.
- Gửi `Authorization: Bearer <accessToken>` cho API cần đăng nhập.
- JSON sử dụng camelCase; gửi `Content-Type: application/json`, trừ API upload multipart.
- ID là UUID. Phân biệt accountId với profile/userId; không dùng lẫn hai loại ID.
- Thời gian sử dụng ISO 8601 có múi giờ, ưu tiên UTC (`Z`); giao diện hiển thị theo múi giờ người dùng.
- Enum hiện dùng giá trị số theo hợp đồng backend; không tự đổi sang chuỗi tên enum.
- Quyền nghiệp vụ chính: `verification.evidence.review`. Admin có thể thu hồi quyền này; client cần xử lý 403 ngay cả khi tài khoản có role Assessor.
- Backend xác định người thao tác từ JWT và kiểm tra quyền/ownership. Không gửi accountId để chọn ví hay hồ sơ của người khác.
- Một số route vẫn giữ tên `verificator`/`verificators`; phải gọi đúng route hiện tại.
- Không tự thêm lớp bọc `{ data: ... }` vào response. Dùng trực tiếp kiểu dữ liệu do controller trả về.

## 3. Xác thực

| Method | Endpoint | Mục đích |
|---|---|---|
| POST | `/api/auth/login` | Đăng nhập |
| POST | `/api/auth/refresh` | Làm mới phiên |
| GET | `/api/auth/me` | Lấy tài khoản hiện tại |
| POST | `/api/auth/logout` | Đăng xuất theo contract AuthController |
| POST | `/api/auth/logout-all` | Đăng xuất mọi phiên |

Login body: `{ "email": "assessor@example.com", "password": "...", "rememberMe": false }`; deviceId/deviceName là tùy chọn. Refresh body: `{ "refreshToken": "..." }`.

AuthResponse gồm accessToken, refreshToken, expiresAt, account, requiresProfileCompletion. Account gồm id, email, role, status, emailVerified, profile, profileComplete, createdAt.

Assessor là tài khoản nhân sự được Admin tạo; không triển khai đăng ký Assessor công khai. Khi phiên hết hạn, chỉ refresh một lần cho các request đồng thời, tránh vòng lặp retry.

## 4. Công việc được phân công

| Method | Endpoint | Response |
|---|---|---|
| GET | `/api/assignments/me?status=0&page=1&pageSize=10` | AssignmentsResponse |
| GET | `/api/assignments/{id}` | AssignmentDetailResponse |

- status tùy chọn: Assigned=0, Completed=1, Overdue=2, Cancelled=3.
- page tối thiểu 1; pageSize được giới hạn 1–100.
- Danh sách chỉ chứa phân công của người hiện tại, sắp xếp theo ngày phân công mới nhất.
- AssignmentsResponse: page, pageSize, totalItems, totalPages, assignments.
- AssignmentResponse: id, projectVerificationId, assignedToUserId, adminId, status, assignedAt, assignmentDueAt, reportDueAt, replacedAt, replacementReason, replacedById, createdAt, updatedAt, scheduledStartAt, scheduledEndAt, meetingStartAt, meetingEndAt, completedAt.
- AssignmentDetailResponse: assignment, project, evidenceModeration, report, assessmentRubric. Các cấu trúc project/minh chứng dùng DTO trong AssignmentContract và AssignmentMapping; không suy diễn thêm field ngoài response.
- Dùng assessmentRubric gắn với hồ sơ để dựng form đánh giá; không luôn thay bằng rubric mới nhất.
- Assessor không được tạo, sửa hay thay người được phân công. Các thao tác quản lý phân công thuộc Admin.

## 5. Lịch rảnh

| Method | Endpoint | Thành công |
|---|---|---|
| GET | `/api/verificators/me/availability?from={ISO}&to={ISO}` | 200, mảng khoảng rảnh |
| POST | `/api/verificators/me/availability` | 201, khoảng vừa tạo |
| PUT | `/api/verificators/me/availability/{id}` | 200, khoảng sau sửa |
| DELETE | `/api/verificators/me/availability/{id}` | 204, không có body |

Body tạo/sửa:

```json
{ "startAt": "2026-12-01T02:00:00Z", "endAt": "2026-12-01T05:00:00Z" }
```

Response item: `{ "id": "<uuid>", "startAt": "...", "endAt": "..." }`.

Quy tắc: from/to bắt buộc; to > from; khoảng truy vấn tối đa 31 ngày. Khoảng rảnh phải bắt đầu trong tương lai, kết thúc sau bắt đầu, dài tối đa 31 ngày và kết thúc trong vòng 365 ngày tính từ hiện tại. Không được chồng lấn khoảng đã có. Không được sửa/xóa làm mất độ bao phủ của lịch đang giữ chỗ, đã xác nhận hoặc đã phân công nếu không còn khoảng khác bao phủ.

Mã lỗi chính: INVALID_AVAILABILITY_RANGE, INVALID_AVAILABILITY_INTERVAL, AVAILABILITY_OVERLAP, AVAILABILITY_IN_USE, AVAILABILITY_NOT_FOUND, AVAILABILITY_CONFLICT. Khi 409, tải lại lịch trước khi cho người dùng thao tác tiếp.

## 6. Họp thẩm định

| Method | Endpoint | Response |
|---|---|---|
| GET | `/api/assignments/{assignmentId}/meeting` | MeetingResponse |
| POST | `/api/assignments/{assignmentId}/meeting` | MeetingResponse, tạo/lấy phòng |
| POST | `/api/assignments/{assignmentId}/meeting/join` | MeetingJoinResponse |
| POST | `/api/verificator/assignments/{assignmentId}/meeting/start` | MeetingResponse |
| POST | `/api/verificator/assignments/{assignmentId}/meeting/end` | MeetingResponse |

Các POST trên không yêu cầu body nghiệp vụ.

MeetingResponse gồm assignmentId, projectVerificationId, roomName, joinUrl, scheduledStartAt, scheduledEndAt, meetingStartAt, meetingEndAt. MeetingJoinResponse gồm assignmentId, projectVerificationId, joinUrl.

Luồng tích hợp: mở chi tiết phân công → lấy thông tin họp → tạo/lấy phòng → tham gia qua joinUrl → Assessor bắt đầu → Assessor kết thúc → cho phép mở bước nộp báo cáo theo trạng thái mới từ server.

Yêu cầu tài khoản active, phân công hợp lệ và chưa bị thay, booking đã confirmed và có lịch hợp lệ. Chỉ Assessor được phân công có quyền start/end. Một số thao tác truy cập phòng cũng dành cho Leader dự án. Không tự dựng joinUrl. Backend dùng Jitsi:BaseUrl; thiếu cấu hình trả JITSI_NOT_CONFIGURED (503). Phòng đã kết thúc không cho join lại và joinUrl không còn được quảng bá.

Mã lỗi cần xử lý: MEETING_ROOM_REQUIRED, INVALID_MEETING_STATE, BOOKING_NOT_CONFIRMED, INVALID_VERIFICATION_SCHEDULE, MEETING_CONFLICT, FORBIDDEN; thêm các lỗi lifecycle do backend trả về.

## 7. Rubric và báo cáo đánh giá

| Method | Endpoint | Thành công |
|---|---|---|
| GET | `/api/reports/rubric` | 200, AssessmentRubric hiện hành |
| POST | `/api/reports` | 201, ReportResponse |
| GET | `/api/report/{assignmentId}` | 200, báo cáo theo phân công |
| GET | `/api/reports/{reportId}` | 200, báo cáo theo ID |

Chú ý `/api/report/{assignmentId}` dùng `report` số ít.

SubmitReportRequest gồm assignmentId, summary, findings, recommendations, assessment; outcome là tùy chọn, nên bỏ để server tự tính.

```json
{
  "assignmentId": "<uuid>",
  "summary": "Tóm tắt kết quả thẩm định",
  "findings": "Các phát hiện qua minh chứng và buổi họp",
  "recommendations": "Các khuyến nghị cải thiện",
  "assessment": {
    "rubricVersion": "1.0",
    "scores": [
      { "code": "requirements", "score": 80, "reason": "Đáp ứng phạm vi đã công bố", "evidence": "Tài liệu yêu cầu, mục 2" }
    ],
    "conditions": [
      { "code": "reviewable_evidence", "passed": true, "reason": "Đã kiểm tra bản demo" }
    ]
  }
}
```

Đây là ví dụ cấu trúc rút gọn, chưa phải payload hợp lệ để submit: phải gửi đủ mọi scores/conditions đúng một lần theo rubric của hồ sơ.

Rubric hiện tại `UAS-SOFTWARE-PROJECT`, version `1.0`, điểm đạt 65:

| Tiêu chí | Code | Trọng số |
|---|---|---|
| Vấn đề và yêu cầu | requirements | 10 |
| Kiến trúc và thiết kế | architecture | 15 |
| Chất lượng mã nguồn | code_quality | 20 |
| Chức năng và hoàn thiện | functionality | 20 |
| Kiểm thử và độ tin cậy | testing | 15 |
| Bảo mật và dữ liệu | security | 10 |
| Tài liệu và triển khai | documentation | 5 |
| Tính ứng dụng | applicability | 5 |

Điều kiện bắt buộc hiện tại: reviewable_evidence, core_functions, no_serious_fabrication, no_concealed_critical_vulnerabilities. Lấy tên/diễn giải từ rubric response, không hardcode làm nguồn dữ liệu chính.

Validation:

- summary: 1–10.000 ký tự; findings: 1–30.000; recommendations: 1–10.000, sau trim.
- score: null hoặc 0–100, tối đa 2 số thập phân. null có nghĩa thiếu minh chứng, không thay bằng 0.
- reason của từng score/condition: 1–4.000 ký tự. Nếu có điểm, evidence bắt buộc 1–4.000 ký tự.
- passed: true/false/null; null là chưa đủ cơ sở xác định.
- rubricVersion phải khớp bản rubric đã gắn cho hồ sơ; không có code lạ hoặc code trùng.
- Có giá trị score/passed null → InsufficientEvidence. Nếu đủ dữ liệu: tổng trọng số >= passingScore và mọi điều kiện đều true → Verified; trường hợp còn lại → Failed.
- Kết quả số: Pending=0, Verified=1, Failed=2, Cancelled=3, InsufficientEvidence=4.

Cho phép gửi khi cuộc họp đã bắt đầu (MeetingStartAt không ở tương lai), kể cả đang họp hoặc đã kết thúc; phân công phải Assigned hoặc Overdue, chưa bị thay; hồ sơ còn Pending và chưa chốt. Mỗi hồ sơ chỉ có một báo cáo cuối cùng. Báo cáo đã gửi bất biến; không triển khai nút sửa/xóa hoặc endpoint update giả định. Submit thành công hoàn tất phân công và chốt kết quả hồ sơ trong cùng giao dịch.

ReportResponse: id, assignmentId, projectVerificationId, summary, findings, recommendations, outcome, submittedByUserId, submittedAt, assessment. assessment gồm rubric, scores, conditions, totalScore, outcome.

Mã lỗi chính: REPORT_EXISTS, ASSIGNMENT_LOCKED, VERIFICATION_LOCKED, MEETING_NOT_STARTED, RUBRIC_NOT_ASSIGNED, RUBRIC_VERSION_MISMATCH, ASSESSMENT_REQUIRED, INVALID_CRITERIA, INVALID_CONDITIONS, INVALID_CRITERION_SCORE, CRITERION_EVIDENCE_REQUIRED, REPORT_OUTCOME_MISMATCH, REPORT_CONFLICT. Khi mất mạng sau submit, tải lại báo cáo trước khi quyết định gửi lại.

## 8. Hồ sơ cá nhân và chuyên môn

| Method | Endpoint | Response |
|---|---|---|
| GET | `/api/profile/me` | AccountResponse |
| PUT | `/api/profile/assessor` | AccountResponse sau cập nhật |
| DELETE | `/api/profile/assessor` | AccountResponse, xóa riêng assessorJson |

```json
{
  "fullName": "Nguyễn Văn A",
  "nickname": "chuyengia-a",
  "avatarUrl": null,
  "coverUrl": null,
  "bio": "Chuyên gia phát triển phần mềm",
  "contactEmail": "expert@example.com",
  "phone": "0901234567",
  "address": "TP. Hồ Chí Minh",
  "assessorJson": {
    "jobTitle": "Senior Engineer",
    "companyName": "Công ty A",
    "yearsOfExperience": 8,
    "expertise": ["Backend", "Cloud"],
    "education": "Thạc sĩ",
    "university": "Đại học A",
    "linkedInUrl": "https://www.linkedin.com/in/example",
    "gitHubUrl": "https://github.com/example",
    "portfolioUrl": "https://example.com"
  }
}
```

PUT thay thế các trường editable, vì vậy khởi tạo form từ dữ liệu hiện tại và gửi đầy đủ các giá trị cần giữ. Avatar/cover chịu kiểm tra tham chiếu tài sản ảnh của backend; dùng luồng upload ảnh hiện có, không giả định chấp nhận URL bất kỳ.

yearsOfExperience là số nguyên 0–80; expertise tối đa 30 mục, mỗi mục 1–100 ký tự, backend trim và loại trùng không phân biệt hoa thường. jobTitle tối đa 150; companyName/education/university tối đa 255. Liên kết tối đa 2.048 ký tự, URL tuyệt đối http/https, không có thông tin đăng nhập. bio tối đa 4.000; contactEmail 255; phone 30; address 2.000. Tên, nickname, email/phone còn tuân thủ validation chung của ProfileService.

Request PUT tối đa 32.768 byte. DELETE giữ nguyên thông tin chung và ảnh; cần xác nhận trên giao diện trước khi xóa dữ liệu chuyên môn. Nếu assessorJson null, hiển thị trạng thái chưa có dữ liệu; không tự khôi phục trường cũ. Lưu thất bại phải giữ bản nháp.

## 9. Ví thu nhập

| Method | Endpoint | Response |
|---|---|---|
| GET | `/api/wallet` | balance, requestKey, entries, withdrawals |
| POST | `/api/wallet/withdrawals` | OwnerWithdrawalResponse |
| POST | `/api/wallet/withdrawals/{id}/cancel` | OwnerWithdrawalResponse |

GET trả balance gồm balance, reserved, available; tối đa 100 entries và 100 withdrawals gần nhất, chưa có phân trang trên endpoint này. Entry gồm id, amount, sourceReference, reason, createdAt.

Body rút tiền:

```json
{
  "requestKey": "<uuid-khong-rong>",
  "amount": "500000",
  "bankName": "Tên ngân hàng",
  "bankAccountNumber": "0123456789",
  "bankAccountHolder": "NGUYEN VAN A"
}
```

Tiền trong response được serialize dạng chuỗi decimal để giữ chính xác. Client giữ amount dạng chuỗi, tránh chuyển sang JavaScript Number khi tính toán/gửi tiền lớn. Gửi số tiền VND nguyên dương, tối đa 999999999999999999, không vượt available. Các trường ngân hàng bắt buộc, tối đa 150 ký tự sau trim.

requestKey dùng để chống tạo trùng: giữ cùng key và cùng payload khi retry một lần rút; chỉ tạo key mới cho yêu cầu mới. Cùng key nhưng khác payload trả IDEMPOTENCY_CONFLICT. GET cung cấp một requestKey mới, không thay key của request đang retry bằng key vừa tải lại.

OwnerWithdrawalResponse: id, amount, bankName, bankAccountNumber, bankAccountHolder, state, createdAt, reviewReason, bankReference.

State: Requested=0, Approved=1, Ordered=2, Paid=3, Rejected=4, Cancelled=5. Chỉ hủy khi Requested; gọi lại với Cancelled được chấp nhận. Các trạng thái Requested/Approved/Ordered giữ tiền reserved. Sau thao tác phải tải lại ví.

Chỉ tài khoản Assessor active được dùng ví. Backend kiểm tra role thực trong database, không dựa riêng role claim đã cache. Assessor không tự cộng tiền hoặc duyệt/xác nhận chi tiền; đây là công việc của Accountant/Admin. Không giả định nộp báo cáo sẽ tự sinh thu nhập vì luồng ví hiện ghi nhận qua nghiệp vụ finance.

Mã lỗi: INVALID_WALLET (403), INVALID_AMOUNT (400), INVALID_REQUEST_KEY (400), INVALID_INPUT (400), INSUFFICIENT_BALANCE (409), IDEMPOTENCY_CONFLICT (409), INVALID_STATE (409), FINANCE_CONFLICT (409), NOT_FOUND (404).

## 10. Thông báo

| Method | Endpoint | Mục đích |
|---|---|---|
| GET | `/api/notifications?page=1&pageSize=20&unreadOnly=false&category=Verification` | Lấy thông báo; category tùy chọn |
| GET | `/api/notifications/unread-count` | `{ "count": 0 }` |
| PATCH | `/api/notifications/{id}/read` | Đánh dấu một mục, 204 |
| PATCH | `/api/notifications/read-all` | Đánh dấu tất cả, 204 |
| GET | `/api/notifications/{id}/target` | `{ "targetUrl": "..." }` |
| GET | `/api/verification-notifications?page=1&pageSize=20` | Danh sách thông báo xác minh chuyên biệt |

Dùng NotificationPage/DTO từ backend cho danh sách chính. Điều hướng qua target endpoint để backend kiểm tra phạm vi truy cập; targetUrl có thể null khi không còn đích hợp lệ. Không tự chuyển thông báo Assessor sang trang quản trị. Cập nhật lại unread-count sau khi đánh dấu.

## 11. Cộng đồng

Đây là chức năng chung cho người dùng đăng nhập, không phải quyền kiểm duyệt của Assessor.

| Method | Endpoint | Mục đích |
|---|---|---|
| GET | `/api/community/posts?page=1&pageSize=10` | Xem feed |
| POST | `/api/community/posts` | Tạo bài, multipart/form-data |
| PUT | `/api/community/posts/{id}` | Sửa bài, multipart/form-data |
| DELETE | `/api/community/posts/{id}` | Xóa bài của mình |
| POST | `/api/community/posts/{id}/react` | Bật/tắt reaction |
| POST | `/api/community/posts/{id}/bookmark` | Bật/tắt bookmark |
| GET | `/api/community/posts/{id}/comments` | Xem bình luận |
| POST | `/api/community/posts/{id}/comments` | Tạo bình luận |
| POST | `/api/community/comments/{commentId}/replies` | Trả lời bình luận |
| PUT | `/api/community/comments/{id}` | Sửa bình luận |
| DELETE | `/api/community/comments/{id}` | Xóa bình luận |
| POST | `/api/community/posts/{id}/report` | Báo cáo bài viết |

Dữ liệu gửi: tạo bài dùng FormData gồm Description, Files; sửa bài gồm Description, KeepMediaUrls, NewFiles. Bình luận/sửa bình luận dùng `{ "content": "..." }`; trả lời dùng `{ "content": "...", "replyToUserId": null }`; báo cáo bài dùng `{ "reason": "..." }`. Dùng chính xác DTO trong `UniNet.Application/DTOs/Community/CommunityDTO.cs` cho body và DTO response của community. Với FormData, không tự đặt Content-Type để trình duyệt sinh boundary. Đối chiếu ownership tại service; không hiển thị thao tác quản trị nội dung cho Assessor.

## 12. Phân quyền và xử lý lỗi

- 400: dữ liệu sai; hiển thị lỗi và giữ form. Model validation có thể dùng ProblemDetails thay vì `{code,message}`.
- 401: thiếu/hết hiệu lực phiên; xử lý refresh/đăng nhập theo luồng xác thực.
- 403: không có quyền, tài khoản không active hoặc sai vai trò; dừng retry tự động.
- 404: đối tượng không tồn tại hoặc không thuộc phạm vi người dùng; quay về danh sách phù hợp.
- 409: trạng thái hoặc dữ liệu xung đột; tải lại dữ liệu và cho phép người dùng đánh giá lại thao tác.
- 503: dịch vụ họp chưa cấu hình; hiển thị thông báo và giữ nguyên hồ sơ.

Các exception nghiệp vụ chính trả `{ "code": "...", "message": "..." }`; không giả định mọi controller hoặc mọi lỗi đều có cùng envelope. Không hiển thị thông báo thành công trước khi nhận response thành công. Hủy request hoặc bỏ qua response cũ khi chuyển tài khoản/hồ sơ.

Ngoài phạm vi quyền Assessor: phân công/thay chuyên gia, duyệt danh tính Student/Partner, khóa/mở người dùng, kiểm duyệt cộng đồng, ghi có ví, duyệt rút tiền, lập lệnh chi, xác nhận chuyển khoản, quản lý nhân sự và quyền hệ thống.

Không xác nhận chức năng tin nhắn riêng chỉ từ tên route giao diện `/assessor-messages`: cần đối chiếu backend/transport tương ứng trước khi tích hợp; tài liệu này không tạo API tin nhắn giả định.

## 13. Thứ tự triển khai đề xuất

1. Tạo API client chung: base URL, token, refresh, lỗi và hủy request.
2. Khai báo DTO/enum từ contract backend; tách kiểu tiền string khỏi kiểu điểm numeric.
3. Tích hợp danh sách và chi tiết phân công, bao gồm lịch/hạn và minh chứng.
4. Tích hợp lịch rảnh và xử lý lịch đang được sử dụng.
5. Tích hợp phòng họp, join, start/end, tải lại trạng thái.
6. Dựng form rubric động và nộp/xem báo cáo cuối cùng.
7. Tích hợp hồ sơ chuyên môn và ví với idempotency.
8. Tích hợp thông báo và cộng đồng dùng API chung.
9. Kiểm tra phân quyền, ownership, lỗi và các bước nghiệp vụ trên môi trường test.

## 14. Checklist nghiệm thu

- [ ] Assessor active đăng nhập và lấy đúng tài khoản hiện tại.
- [ ] Danh sách phân công chỉ có công việc của mình; lọc/phân trang đúng.
- [ ] Không đọc được phân công/báo cáo của Assessor khác.
- [ ] Không truy cập nghiệp vụ thẩm định khi quyền bị thu hồi.
- [ ] Lịch rảnh xử lý đúng múi giờ, chồng lấn và lịch đang dùng.
- [ ] Start/end phòng họp chỉ dành cho người được phân công; booking chưa confirmed bị chặn.
- [ ] Chưa kết thúc họp không thể nộp báo cáo.
- [ ] Form dùng rubric được gắn cho hồ sơ, gửi đủ mã và xử lý null đúng nghĩa.
- [ ] Điểm/điều kiện cho ra Verified, Failed, InsufficientEvidence đúng backend.
- [ ] Submit trùng hoặc báo cáo đã có không sinh báo cáo thứ hai; không có sửa/xóa báo cáo.
- [ ] Cập nhật/xóa assessorJson giữ đúng dữ liệu chung; lỗi lưu không mất draft.
- [ ] Ví giữ chính xác số tiền; cùng requestKey không tạo hai yêu cầu rút.
- [ ] Chỉ hủy yêu cầu Requested; người khác không thể hủy hộ.
- [ ] Tài khoản sai role/suspended không dùng được ví.
- [ ] Thông báo chỉ điều hướng tới tài nguyên còn được phép truy cập.
- [ ] Bài/bình luận chỉ sửa/xóa theo ownership; không có thao tác kiểm duyệt Assessor.
- [ ] 401/403/404/409/503 được xử lý; request lỗi không làm UI báo thành công.

## 15. Nguồn đối chiếu trong repository

- `UniNet.API/Controllers/AssignmentController.cs`; `UniNet.Application/Services/AssignmentService.cs`.
- `UniNet.API/Controllers/VerificatorAvailabilityController.cs`; `UniNet.Application/Services/VerificatorAvailabilityService.cs`.
- `UniNet.API/Controllers/MeetingController.cs`; `UniNet.Application/Services/MeetingService.cs`.
- `UniNet.API/Controllers/ReportController.cs`; `UniNet.Application/Services/ReportService.cs`; `UniNet.Application/Services/ProjectAssessmentPolicy.cs`.
- `UniNet.Application/DTOs/Projects/AssignmentContract.cs`, `MeetingContract.cs`, `ReportContracts.cs`, `ProjectAssessmentContracts.cs`, `VerificatorAvailabilityContracts.cs`.
- `UniNet.API/Controllers/ProfileController.cs`; `UniNet.Application/Services/AssessorProfileService.cs`; `UniNet.Application/DTOs/Auth/AuthContracts.cs`.
- `UniNet.API/Controllers/FinanceController.cs`; `UniNet.Application/Services/FinanceService.cs`; `UniNet.API/Services/FinanceJsonAttribute.cs`.
- `UniNet.API/Controllers/NotificationsController.cs`, `VerificationNotificationsController.cs`, `CommunityController.cs`.
- `UniNet.Application/Services/NotificationPermissionPolicy.cs`.

Tài liệu được rà soát tĩnh theo controller/service/DTO; chưa xác nhận bằng việc gọi API thực tế trong lần soạn này.
