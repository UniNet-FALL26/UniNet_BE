# UniNet Dashboard — Kế hoạch triển khai quản lý thẩm định dự án

Scope update 8 November 2026: Level 1 evidence review has been retired. Any L1 endpoints, fields, workers or planned prerequisites below are historical and must not be implemented. Current API changes are documented in [Level 1 removal](handoffs/remove-verification-l1-prerequisite.md). Project creation content moderation remains active.


Ngày rà soát: 03/11/2026.
Source: `D:\SEP490\UniNet_Dashboard` và backend `D:\SEP490\UniNet_BE`.
Phương pháp: đọc source, đối chiếu controller, DTO và service; chưa chạy UI/API với dữ liệu thực. Các đường dẫn source dưới đây tương đối với repository tương ứng.

## 1. Kết luận và phạm vi

Dashboard thiếu module Admin quản lý hồ sơ thẩm định dự án: danh sách, chi tiết, phân công Assessor/Verificator, thay người, xem báo cáo và theo dõi quá hạn. Backend đã có phần lớn API cho các chức năng này. Không cần thiết kế một workflow phê duyệt báo cáo mới: báo cáo hợp lệ được nộp sẽ tự hoàn thành phân công và chốt hồ sơ trong một transaction.

Phạm vi ưu tiên: bổ sung module Admin, giữ luồng Assessor trên ứng dụng UniNet theo chính sách đăng nhập hiện có. Việc mở Dashboard cho Assessor là phạm vi mở rộng riêng, không phải chỉ thêm route.

Phân biệt ba khái niệm:
- Verifier: xác minh tài khoản sinh viên/đối tác; không phải người thẩm định dự án.
- Assessor: vai trò backend của thẩm định viên dự án; source UI cũ thường đặt tên `verificator`.
- Moderator: kiểm duyệt dự án/nội dung; không mặc nhiên có quyền phân công hoặc đọc API Admin thẩm định.

## 2. Hiện trạng được xác nhận từ source

| Khu vực | Hiện trạng | Việc cần làm |
|---|---|---|
| `src/pages/admin/ProjectsPage.tsx` | Số liệu và các dòng dự án hardcode, chưa có luồng API thẩm định | Tạo module chuyên biệt; không sử dụng số liệu mẫu như dữ liệu vận hành |
| `src/App.tsx` | Có `/admin/projects`; chưa có route Admin hồ sơ/phân công/báo cáo; không mount các trang `verificator` | Thêm route Admin và menu |
| `src/pages/verificator/ProjectsPage.tsx`, `ProjectDetailPage.tsx` | Có code danh sách, chi tiết phân công, bằng chứng, meeting/report nhưng chưa nối route | Tái sử dụng có chọn lọc component đọc dữ liệu; không coi là chức năng đã chạy được |
| `src/pages/verificator/VerificatorAvailabilityPage.tsx` | Có trang lịch rảnh nhưng chưa mount | Chỉ mở nếu triển khai phạm vi Assessor web |
| `src/services/verificator-assignments.service.ts` | Có API cá nhân/meeting/report, thiếu bộ thao tác Admin create/replace/candidates | Tạo service Admin riêng |
| `src/services/auth.service.ts` | Chủ động từ chối role `2`, `assessor`, `verificator`, hướng sang app UniNet | Giữ trong phạm vi Admin; nếu mở web phải sửa đồng bộ và cập nhật test |
| `src/utils/dashboard-role-access.ts` | Chỉ nhận verifier/moderator/accountant/admin | Không ánh xạ Assessor thành Verifier để lách quyền |
| `tests/assessor-denial-browser-check.cjs` | Kiểm thử rõ hành vi từ chối Assessor | Đây là chủ đích hiện tại, không đơn thuần thiếu mapping |
| `src/types/verificator-assignments.ts` | Thiếu assessment/rubric, thiếu outcome 4; ProjectStatus không khớp backend | Đồng bộ hợp đồng trước khi dùng lại |
| `src/services/verificator-report.service.ts` và report panel | Chỉ mô hình Đạt/Không đạt, chưa có assessment bắt buộc | Không dùng nguyên trạng cho nộp báo cáo |
| `src/utils/notification-targets.ts` | Chưa có allowlist chi tiết hồ sơ thẩm định mới | Bổ sung khi thêm route |

Lỗi dữ liệu cụ thể: backend `ProjectStatus` là Pending=0, Active=1, Completed=2, Rejected=3. Kiểu UI cũ lại dùng Planning/Recruiting/InProgress/Completed… nên có thể hiển thị sai trạng thái dự án.

## 3. Danh mục chức năng cần triển khai

### 3.1. Danh sách hồ sơ thẩm định — P0

Đề xuất route `/admin/project-verifications`, menu “Thẩm định dự án”, tách với kiểm duyệt dự án và xác minh tài khoản.

Ba chế độ danh sách: Tất cả, Chờ phân công, Quá hạn. API riêng của hai hàng đợi chỉ hỗ trợ page/pageSize; không giả định chúng nhận toàn bộ bộ lọc của danh sách chung.

Bảng tối thiểu: mã hồ sơ, tên dự án, trạng thái thẩm định, trạng thái đặt lịch, lịch hẹn, thời điểm gửi, mở chi tiết. Endpoint list hiện không trả tên người thẩm định, deadline phân công hay tình trạng thanh toán chi tiết; chỉ hiển thị các cột đó sau khi có API bổ sung/enrichment phù hợp. Tránh gọi detail cho từng dòng.

Bộ lọc danh sách chung: status, from, to theo SubmittedAt; phân trang server. Chưa có tìm kiếm tên dự án, sort tùy chọn hoặc API thống kê tổng quan cho module này. Không tìm kiếm trên một trang rồi trình bày như kết quả toàn hệ thống.

“Chờ phân công” từ backend: Pending, chưa finalized, booking Confirmed, có AssignmentEligibleAt và không còn assignment chưa Cancelled. Đây là hàng đợi sơ bộ; thao tác assign vẫn có thể bị chặn bởi lịch đã qua, bằng chứng lỗi thời hoặc thanh toán không hợp lệ.

“Quá hạn”: hồ sơ Pending chưa finalized, lịch kết thúc đã qua mà chưa kết thúc họp, hoặc assignment chưa hoàn thành/thay thế có deadline đã qua hoặc status Overdue. Không chỉ kiểm tra badge Overdue.

### 3.2. Chi tiết hồ sơ — P0

Đề xuất `/admin/project-verifications/:verificationId`, các tab:
1. Tổng quan: Project, Verification, PackageId, booking/reservation, lịch hẹn và thời gian họp thực tế, hủy/chốt kết quả.
2. Bằng chứng cấp 1: snapshot nguồn, kết quả, attempts, thời điểm, source/policy hash, tình trạng current và phạm vi giới hạn đánh giá tự động.
3. Phân công: assignment hiện tại, các assignment cũ, deadline, lý do/thời điểm thay người, liên kết assignment thay thế.
4. Báo cáo: summary, findings, recommendations, outcome, thời gian gửi, rubric snapshot, điểm và điều kiện bắt buộc.
5. Thanh toán/hoàn tiền: giao dịch, trạng thái, số tiền, requiresManualReview/failureReason, các yêu cầu hoàn tiền.

Lưu ý response Admin có wrapper `{ detail: ProjectVerificationDetailResponse }`.
Danh sách assignment trong detail hồ sơ chỉ là bản thu gọn (`id`, `assignedToUserId`, `status`, `assignedAt`, `report`). Muốn xem deadline và replacement metadata phải tải `GET /api/assignments/{id}` khi mở assignment hoặc mở rộng DTO backend. Không giả định mọi trường có trong detail hồ sơ.

### 3.3. Phân công người thẩm định — P0

Luồng: mở hồ sơ → “Phân công” → tải candidates theo verificationId → chọn người → POST assignment → tải lại detail và hàng đợi.

Chỉ dùng `userId` do API candidates trả về làm `assignedToUserId`; đây là UserProfile.Id, không phải Account.Id. Không lấy danh sách staff bất kỳ rồi cho phép chọn.

Backend yêu cầu:
- Hồ sơ Pending, chưa finalized, chưa bắt đầu/kết thúc họp và chưa có report.
- Chưa có assignment Assigned, Overdue hoặc Completed cho hồ sơ.
- Booking Confirmed và có BookingConfirmedAt; lịch hợp lệ và bắt đầu trong tương lai.
- Dự án Completed; kết quả kiểm tra bằng chứng cấp 1 Passed, đúng liên kết và còn khớp source/policy hiện tại.
- Có thanh toán Success đúng package, người yêu cầu, reservation; FailureReason null; không có refund Pending/Success.
- Người được chọn là Assessor Active, không phải người yêu cầu/chủ dự án/thành viên dự án; lịch rảnh bao phủ toàn bộ buổi họp và không xung đột booking/phân công khác theo backend.

Candidates trả mảng `{ userId, fullName }`, chưa có chuyên môn, tải công việc hoặc lý do từng người bị loại. Khi rỗng, hiển thị không có người phù hợp và cho tải lại; không tự chọn ngoài danh sách.

### 3.4. Thay người thẩm định (reassign) — P0

Luồng: chọn assignment đang hoạt động → dialog tên người hiện tại/lịch/deadline → chọn người khác và nhập lý do 1–2000 ký tự sau trim → POST replace → chuyển sang assignment mới, tải lại lịch sử.

Backend chỉ cho assignment Assigned/Overdue, chưa có report, chưa từng được thay. Người mới phải khác người cũ và còn đủ điều kiện lịch. Assignment cũ chuyển Cancelled, lưu ReplacedAt/ReplacementReason/ReplacedById; response là assignment mới. Không sửa AssignedToUserId trực tiếp, không xóa lịch sử.

Điểm đặc biệt: replace cho phép hồ sơ đã bắt đầu hoặc kết thúc họp, và không bắt buộc lịch bắt đầu trong tương lai; vẫn kiểm tra hồ sơ Pending, không report, booking/payment/cấp 1 hợp lệ. Không chặn reassign trên UI chỉ vì meeting đã diễn ra. Khi meeting đã kết thúc, assignment mới được đặt AssignmentDueAt và ReportDueAt = thời điểm thay +48 giờ; nếu chưa kết thúc thì AssignmentDueAt = ScheduledEndAt và ReportDueAt null.

**Khoảng trống backend cần xử lý:** API candidates đang gọi GetSchedulableVerificationAsync với `replacement=false`, trong khi replace dùng `replacement=true`. Vì vậy dialog không lấy được candidates khi lịch đã qua hoặc họp đã bắt đầu, dù endpoint replace có thể cho phép thay. Cần thêm endpoint candidates theo assignment hoặc mode replacement được kiểm tra phía server, dùng cùng điều kiện với replace. Không giải quyết bằng dropdown staff không kiểm tra lịch.

### 3.5. Xem báo cáo và kết quả — P0

Admin được xem, không có bước approve/reject báo cáo. Backend hiện hỗ trợ một báo cáo cuối cùng cho hồ sơ; báo cáo bất biến sau nộp. Mảng reports không có nghĩa là được phép lập hội đồng nhiều báo cáo hoặc sửa nhiều phiên bản.

Hiển thị rubric/version đã gắn vào hồ sơ và kết quả đã lưu trong assessment; không chấm lại báo cáo lịch sử theo rubric mới. Endpoint reports thu gọn thiếu submittedByUserId; nếu cần thông tin này tải report detail. API cũng chưa trả sẵn tên người nộp, cần bổ sung/join nguồn có quyền hoặc hiển thị ID có nhãn rõ ràng.

Kết quả hợp lệ: Pending=0, Verified=1, Failed=2, Cancelled=3, InsufficientEvidence=4. “Chưa đủ cơ sở đánh giá” là kết quả đã chốt của lần thẩm định này, không phải Pending.

### 3.6. Theo dõi cuộc họp và quá hạn — P1

Admin xem lịch dự kiến/thời gian thực tế qua detail, dùng hàng đợi quá hạn để điều phối và thay người khi đủ điều kiện. Không thêm nút Admin bắt đầu/kết thúc cuộc họp: PUT assignment với StartMeeting/EndMeeting luôn trả 403; các endpoint start/end yêu cầu Assessor được phân công. Khi report hợp lệ được nộp, backend tự Completed và finalized; không cần nút “Hoàn thành” trong luồng chính.

Không mặc định Admin có quyền join/create phòng chỉ vì controller meeting có Authorize chung; quyền chi tiết nằm ở service và phải được kiểm tra trước khi mở chức năng tương ứng.

### 3.7. Hoàn tiền và khôi phục booking đã thanh toán — P1

Tích hợp từ tab Thanh toán, liên kết với module tài chính hiện có để tránh hai nguồn trạng thái.

Tạo yêu cầu hoàn tiền: transaction thuộc hồ sơ, payment Success; amount là số nguyên VND >0 và không vượt số dư sau các khoản đã được giữ cho hoàn tiền. Reason 1–2000 ký tự. Tạo thành công là yêu cầu Pending, không đồng nghĩa tiền đã hoàn; xử lý duyệt/chi trả theo module finance.

Recover paid booking: yêu cầu transactionId và reason. Chỉ áp dụng hồ sơ Pending chưa chốt/chưa họp, lịch tương lai, booking Expired/Held có reservation, không có assignment/report; payment Success khớp hồ sơ/package/người yêu cầu/reservation, thuộc trường hợp late-payment/booking-payment review; không refund Pending/Success hoặc giao dịch Pending/Success cạnh tranh. Backend còn kiểm tra cấp 1 và capacity. Thành công chuyển booking Confirmed, cập nhật AssignmentEligibleAt và xóa FailureReason của payment; chưa tạo assignment.

Không tự động gọi recovery khi gặp lỗi thanh toán. Hiển thị thao tác có lý do và dữ liệu giao dịch cụ thể; server là nguồn xác nhận cuối.

### 3.8. Thông báo — P1

Backend NotificationTargets hiện đưa Admin từ ProjectVerification về `/admin/projects`, Assessor về `/assessor-assessments/{assignmentId}` trên app. Khi tạo màn hình chi tiết Admin, cập nhật backend target cho ProjectVerification sang route mới có verificationId và thêm allowlist frontend. Target EvidenceModeration cần được xem xét riêng vì ID là moderationId, không phải verificationId.

Giữ xác thực target phía backend và kiểm tra route frontend. Sau assign/replace, backend đã enqueue thông báo cho người mới; tránh frontend gửi thêm thông báo trùng.

## 4. Bảng API hiện có

Tất cả đường dẫn dưới đây bao gồm `/api`. Trong service Dashboard, apiClient đã có base `/api`, chỉ truyền phần sau tiền tố này.

| Method | Endpoint | Dữ liệu / mục đích |
|---|---|---|
| GET | `/api/admin/project-verifications` | status?, from?, to?, page=1, pageSize=10 |
| GET | `/api/admin/project-verifications/pending-assignment` | page, pageSize |
| GET | `/api/admin/project-verifications/overdue` | page, pageSize |
| GET | `/api/admin/project-verifications/{verificationId}` | `{ detail: ... }` |
| GET | `/api/admin/project-verifications/{verificationId}/reports` | Mảng VerificationReportResponse |
| GET | `/api/assignments` | Admin; status?, page, pageSize; mảng nằm ở assignments |
| GET | `/api/assignments/{assignmentId}` | assignment, project, evidenceModeration, report, assessmentRubric |
| GET | `/api/assignments/availability?projectVerificationId={id}` | Mảng `{ userId, fullName }`; lưu ý hạn chế replacement ở mục 3.4 |
| POST | `/api/assignments` | `{ projectVerificationId, assignedToUserId }`; trả 201 AssignmentResponse |
| POST | `/api/assignments/{assignmentId}/replace` | `{ assignedToUserId, reason }`; trả 201 assignment mới |
| PUT | `/api/assignments/{assignmentId}` | `{ action }`; không dùng cho StartMeeting/EndMeeting của Admin |
| GET | `/api/report/{assignmentId}` | Report hiện tại; `report` số ít |
| GET | `/api/reports/{reportId}` | Report đầy đủ theo reportId |
| GET | `/api/reports/rubric` | Rubric hiện hành; không thay thế snapshot của hồ sơ |
| GET | `/api/admin/project-verifications/{id}/refunds` | Danh sách refund |
| POST | `/api/admin/project-verifications/{id}/refunds` | `{ transactionId, amount, reason }` |
| POST | `/api/admin/project-verifications/{id}/recover-paid-booking` | `{ transactionId, reason }` |

Role Admin Active và permission `verification.evidence.review` được kiểm tra tại service. Tạo refund yêu cầu thêm `finance.refund.create` (đối chiếu hằng RefundCreate trước triển khai nếu permission catalog thay đổi). Không cấp các API Admin này cho Verifier/Moderator/Accountant chỉ dựa vào menu.

## 5. Nếu mở rộng cho Assessor trên Dashboard

Đây là phạm vi tùy chọn sau module Admin. Phải thống nhất lại chính sách sản phẩm vì source hiện chủ động từ chối vai trò này.

Các phần phải sửa đồng bộ: types/auth, auth.service, dashboard-role-access, dashboard-session nếu có allowlist vai trò, ProtectedRoute, navigation/default route, App.tsx, notification targets và bộ test từ chối Assessor hiện tại. Dùng role frontend `assessor`, backend `Assessor`; giữ `verifier` riêng.

Các màn hình: phân công của tôi; chi tiết/bằng chứng; lịch rảnh; cuộc họp; lập/xem báo cáo. Có thể tái sử dụng các file `verificator` sau khi sửa hợp đồng.

API Assessor:
- GET `/api/assignments/me?status=&page=&pageSize=`; GET `/api/assignments/{id}`.
- GET/POST `/api/assignments/{id}/meeting`; POST `/api/assignments/{id}/meeting/join`.
- POST `/api/verificator/assignments/{id}/meeting/start` và `/end`.
- GET `/api/verificators/me/availability?from=&to=`; POST cùng collection, PUT/DELETE `/{id}`; payload save `{ startAt, endAt }`.
- POST `/api/reports` và các API đọc report ở trên.

Lịch rảnh: khoảng truy vấn >0 và tối đa 31 ngày; khoảng tạo/sửa phải bắt đầu trong tương lai, dài tối đa 31 ngày, kết thúc trong 365 ngày; không chồng lấn. Không sửa/xóa làm mất bao phủ booking/phân công đang được bảo vệ.

### 5.1. Form báo cáo bắt buộc sửa

Payload:
```json
{
  "assignmentId": "<assignment UUID>",
  "summary": "Tóm tắt",
  "findings": "Phát hiện và đối chiếu",
  "recommendations": "Khuyến nghị",
  "assessment": {
    "rubricVersion": "<version từ assessmentRubric của hồ sơ>",
    "scores": [
      { "code": "<mỗi criterion đúng một lần>", "score": 80, "reason": "Lý do", "evidence": "Minh chứng" }
    ],
    "conditions": [
      { "code": "<mỗi condition đúng một lần>", "passed": true, "reason": "Lý do" }
    ]
  }
}
```
Ví dụ trên minh họa cấu trúc; phải điền đủ toàn bộ criteria/conditions từ snapshot, không gửi chỉ một phần như ví dụ.

- Summary tối đa 10.000, findings 30.000, recommendations 10.000 ký tự; tất cả bắt buộc.
- Mỗi score trong 0–100, tối đa hai chữ số thập phân; null là thiếu minh chứng, không biến thành 0.
- Reason mỗi criterion/condition bắt buộc, tối đa 4.000 ký tự. Criterion có score phải có evidence 1–4.000 ký tự.
- Condition là true/false/null; null nghĩa chưa đủ cơ sở xác định.
- Outcome do server tính; nên bỏ trường outcome trong request để tránh mâu thuẫn kết quả.
- Rubric hiện tại `UAS-SOFTWARE-PROJECT` v1.0: requirements 10%, architecture 15%, code_quality 20%, functionality 20%, testing 15%, security 10%, documentation 5%, applicability 5%; ngưỡng đạt 65.
- Bốn điều kiện hiện tại: reviewable_evidence, core_functions, no_serious_fabrication, no_concealed_critical_vulnerabilities. Render từ dữ liệu rubric thay vì hardcode số lượng.
- Có score/condition null → InsufficientEvidence. Nếu đủ thông tin, tổng điểm >=65 và mọi điều kiện true → Verified; còn lại Failed. Không làm tròn tổng điểm trước khi xác định đạt.
- Chỉ nộp khi assignment Assigned/Overdue, chưa bị thay, hồ sơ Pending chưa finalized, meeting đã bắt đầu và kết thúc hợp lệ, chưa có report. Nộp thành công khóa chỉnh sửa và chốt kết quả.

## 6. Thiết kế triển khai frontend đề xuất

Tạo mới:
- `src/types/admin-project-verifications.ts`: DTO list/detail/payment/refund/booking và các enum đúng backend.
- `src/services/admin-project-verifications.service.ts`: list, queues, detail, reports, refunds, recovery.
- `src/services/admin-assignments.service.ts`: list/detail/candidates/create/replace.
- `src/pages/admin/ProjectVerificationsPage.tsx`, `ProjectVerificationDetailPage.tsx`.
- `src/components/project-verifications/AssignVerificatorDialog.tsx`, `ReplaceVerificatorDialog.tsx`, `AssignmentHistoryPanel.tsx`, `VerificationReportView.tsx`, `VerificationPaymentsPanel.tsx`.

Cập nhật `App.tsx`, `data/navigation.ts`, `utils/notification-targets.ts`; xử lý link từ `/admin/projects` sang module mới khi phù hợp. Tách component đọc báo cáo khỏi form gửi để Admin không nhận nhầm nút sửa/nộp.

Dùng apiClient chung, cancellation khi đổi filter/trang; loading, empty, forbidden, not found và retry rõ ràng. Reset page về 1 khi đổi bộ lọc. Khóa nút trong lúc gửi, không optimistic update trạng thái chốt; tải lại server sau mutation. Với lỗi mất mạng khi POST, tải lại hồ sơ trước khi cho gửi lại để tránh tạo trùng khi server đã xử lý.

Phân biệt ID trong type và tên biến: projectId, verificationId, assignmentId, reportId, transactionId, profile userId, accountId. Không dùng route ProjectDetail cũ với verificationId vì component đó gọi API assignment.

## 7. Khoảng trống backend / dữ liệu cần xử lý

| Mức | Khoảng trống | Hướng xử lý |
|---|---|---|
| P0 cho thay người sau giờ họp | Candidates áp dụng điều kiện assign ban đầu | Endpoint/mode candidates cho replacement dùng cùng điều kiện replace, quyền Admin và validation server |
| P1 | Assignment trong detail hồ sơ thiếu deadline/replacement/tên người | Mở rộng DTO hoặc tải assignment detail khi người dùng mở từng mục |
| P1 | List assignment không có tên dự án/tên thẩm định viên | Enrich query DTO nếu cần bảng điều phối toàn hệ thống; tránh N+1 |
| P1 | Notification Admin về trang projects mẫu | Map target đến chi tiết verification và cập nhật allowlist |
| P2 | Không có search/tổng quan KPI toàn hệ thống cho module | Bổ sung API nếu UI yêu cầu; không tính từ một page |
| Vận hành | Hồ sơ cũ có thể thiếu rubric/link cấp 1 | Xử lý migration/dữ liệu có kiểm soát; không để frontend tự gắn rubric hiện hành hoặc bỏ qua cấp 1 |

Chưa thấy API sửa/xóa report, Admin duyệt report, mở lại hồ sơ đã finalized, đổi lịch sau thanh toán, CRUD lịch rảnh của người khác hoặc xóa assignment tùy ý trong các controller đã rà soát. Những chức năng này là yêu cầu mới cần thiết kế backend; không tự bịa endpoint để triển khai UI.

## 8. Lỗi nghiệp vụ cần hiển thị

| Nhóm | Mã lỗi tiêu biểu | Hành vi UI |
|---|---|---|
| Quyền | FORBIDDEN, UNAUTHORIZED | Thông báo quyền/đăng nhập qua cơ chế session chung |
| Hồ sơ | VERIFICATION_NOT_FOUND, VERIFICATION_LOCKED | Tải lại hoặc quay danh sách; khóa mutation |
| Điều kiện | BOOKING_NOT_CONFIRMED, PAYMENT_REQUIRED, INVALID_VERIFICATION_SCHEDULE, PROJECT_NOT_COMPLETED | Giải thích điều kiện còn thiếu |
| Cấp 1 | LEVEL1_REQUIRED, LEVEL1_STALE, LEVEL1_LINK_REQUIRED | Hiển thị cần xử lý bằng chứng/migration; không bỏ qua kiểm tra |
| Phân công | ASSIGNMENT_EXISTS, ASSIGNMENT_LOCKED, SAME_VERIFICATOR, INVALID_REPLACEMENT_REASON, VERIFICATOR_UNAVAILABLE | Giữ form/lý do, làm mới candidates và trạng thái |
| Tranh chấp | ASSIGNMENT_CONFLICT, VERIFICATION_CONFLICT, REPORT_CONFLICT | Tải lại dữ liệu; không retry POST mù |
| Báo cáo | REPORT_EXISTS, REPORT_NOT_FOUND, ASSESSMENT_REQUIRED, RUBRIC_NOT_ASSIGNED, RUBRIC_VERSION_MISMATCH, REPORT_OUTCOME_MISMATCH | Phân biệt chưa có report và lỗi thật; đồng bộ rubric |
| Recovery/refund | BOOKING_RECOVERY_NOT_ALLOWED, PAYMENT_NOT_RECOVERABLE, BOOKING_CAPACITY_UNAVAILABLE, INVALID_REFUND_AMOUNT | Giữ dữ liệu nhập, giải thích và tải lại số dư/capacity |

Thêm mapping validation assessment: INVALID_CRITERIA, INVALID_CONDITIONS, INVALID_CRITERION_SCORE, INVALID_CRITERION_REASON, CRITERION_EVIDENCE_REQUIRED, INVALID_CRITERION_EVIDENCE, INVALID_CONDITION_REASON. Không chỉ dựa vào HTTP status; đọc code lỗi từ backend và ProblemDetails khi có.

## 9. Thứ tự thực hiện

1. P0: đồng bộ types/enum, service Admin, route/menu, danh sách và chi tiết chỉ đọc.
2. P0: assign, candidates, lịch sử assignment và đọc report/assessment.
3. P0: sửa backend candidates cho replacement; làm reassign đủ cả trước và sau cuộc họp.
4. P1: hàng đợi quá hạn, payment/refund/recovery và notification deep link; kiểm tra quyền thực tế.
5. P2: tìm kiếm/KPI/enrichment tùy nhu cầu.
6. Chỉ khi mở phạm vi Assessor web: thay chính sách auth và hoàn thiện lịch rảnh/meeting/form rubric, cập nhật test tương ứng.

## 10. Tiêu chí nghiệm thu

- Admin đủ quyền xem được dữ liệu thật, phân trang/lọc đúng; không còn số liệu mẫu trong module thẩm định.
- Không có quyền thì API từ chối kể cả truy cập URL trực tiếp; Verifier không trở thành Assessor.
- Hồ sơ hợp lệ assign thành công; refresh vẫn thấy assignment và hồ sơ rời hàng đợi chờ phân công.
- Double click/hai Admin assign đồng thời không tạo hai assignment hoạt động; UI xử lý conflict và tải lại.
- Không assign hồ sơ chưa paid/confirmed, lịch quá khứ, thiếu hoặc stale cấp 1, đã có report.
- Candidate không rảnh/xung đột/thành viên dự án không được chọn; tình huống danh sách rỗng có hướng xử lý rõ.
- Replace đúng người, bắt buộc lý do; response assignment mới; lịch sử cũ Cancelled và lưu đủ replacement metadata.
- Thay người được cả trường hợp họp đã bắt đầu/kết thúc khi backend cho phép; candidates không chặn sai vì lịch đã qua.
- Report hiển thị đủ assessment và outcome InsufficientEvidence; báo cáo cuối không có nút sửa/approve không được hỗ trợ.
- Refund không vượt số dư được giữ; hiển thị yêu cầu Pending đúng nghĩa. Recovery không tự phân công và không chạy khi không đủ điều kiện.
- Thông báo Admin mở đúng hồ sơ; ID không bị nhầm giữa project/verification/assignment.
- Nếu làm Assessor web: nộp báo cáo thiếu assessment bị chặn; đủ rubric cho kết quả khớp server; meeting chưa hoàn tất không nộp được; người cũ không thể nộp sau reassign.

Kiểm tra triển khai: build/typecheck/lint theo package scripts của Dashboard, test API hợp đồng và các kịch bản UI có dữ liệu phù hợp. Các test quyền Assessor hiện tại phải tiếp tục pass nếu phạm vi chỉ là Admin; chỉ đổi chúng khi chính sách web Assessor được thay đổi rõ ràng.

## 11. Source làm căn cứ

Dashboard: `src/App.tsx`, `src/data/navigation.ts`, `src/pages/admin/ProjectsPage.tsx`, `src/pages/verificator/*`, `src/components/verificator/*`, `src/services/verificator-*.service.ts`, `src/types/verificator-assignments.ts`, `src/services/auth.service.ts`, `src/utils/dashboard-role-access.ts`, `src/utils/notification-targets.ts`, `tests/assessor-denial-browser-check.cjs`.

Backend: `UniNet.API/Controllers/{AdminProjectVerification,Assignment,ProjectVerification,Report,Meeting,VerificatorAvailability}Controller.cs`; `UniNet.Application/DTOs/Projects/{AdminProjectVerificationContracts,ProjectVerificationContract,AssignmentContract,ReportContracts,ProjectAssessmentContracts,VerificatorAvailabilityContracts}.cs`; `UniNet.Application/Services/{AdminProjectVerificationService,AssignmentService,ReportService,ProjectAssessmentPolicy,VerificationEligibility,VerificationAvailabilityCalendar,VerificatorAvailabilityService,NotificationTargets,NotificationPermissionPolicy}.cs`; `UniNet.Domain/Enums/{ProjectVerificationStatus,VerificationAssignmentStatus,VerificationBookingStatus,ProjectStatus}.cs`.
