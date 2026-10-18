# Đợt D + E — Report một lần, bỏ Admin review, audit refund

Ngày: 18/10/2026. Giữ nguyên tên các bảng lịch sử, ngừng sử dụng trong luồng vận hành mới theo lựa chọn người dùng.

## Report và phân công

- Verificator được phân công bắt buộc nhận; không có accept/reject case. Admin có thể thay người trước
  khi có report, kể cả sau khi họp đã kết thúc. Người cũ bị thay không còn quyền nộp. Lịch sử thay người
  nằm ở các assignment, người phân công của assignment mới và ReplacedById/ReplacedAt/ReplacementReason.
- Assignment mới vẫn kiểm tra lịch Confirmed, payment cùng reservation chưa refund, cấp 1 hiện hành,
  người nhận không thuộc project và không trùng lịch. Case có report không thể được phân công lại.
- `POST /api/reports` chỉ nhận assignment đang hiệu lực, chưa có report và meeting đã bắt đầu/kết thúc
  hợp lệ (MeetingEndAt >= MeetingStartAt và không ở tương lai). Không bỏ điều kiện meeting.
- Submit hợp lệ lưu summary/findings/recommendations/outcome/người nộp/thời gian, hoàn tất assignment,
  chốt case Verified/Failed và tạo một sự kiện ReportAuditLogs trong cùng transaction Serializable.
  Không cần Admin duyệt. Báo cáo Failed không tự hoàn tiền.
- Mỗi assignment tối đa một report; mỗi case mới tối đa một report. Unique index và FK ghép đảm bảo
  report không thể giả liên kết sang case khác để vượt giới hạn. Xung đột đồng thời trả REPORT_CONFLICT;
  gửi lại cho assignment đã có report trả REPORT_EXISTS.
- Report sau submit không có API sửa/xóa; EF cũng chặn cập nhật/xóa. ReportAuditLogs chỉ lưu sự kiện,
  không sao chép nội dung report. Trường Version cũ của audit được giữ cho lịch sử, sự kiện mới dùng 1.
- GET `/api/report/{assignmentId}` và GET `/api/reports/{reportId}` vẫn theo quyền creator, Admin,
  đúng verificator. Không tự mở quyền cho mọi thành viên.
- Bỏ GET/POST `/api/reports/{reportId}/versions`, DTO CreateReportVersionRequest và các trường version,
  revisionReason/rootReportId trong ReportResponse. Mọi detail/mapping chỉ đọc VerificationReports.
- Bỏ POST `/api/admin/project-verifications/{id}/reviews`, ReviewAsync, contract Uphold/ReassessmentRequired
  và Actions trong admin detail. Không có đường sửa kết luận report qua Admin review.

## Audit hoàn tiền

RefundTransactions bổ sung RequestedByUserId, RequestedAt và RequestSource:

| RequestSource | Giá trị | Actor |
| --- | --- | --- |
| LegacyUnknown | 0 | Không suy diễn khi lịch sử không đủ bằng chứng |
| System | 1 | RequestedByUserId null; không gán Admin giả |
| Admin | 2 | Profile của Admin đã xác thực |

Yêu cầu refund thủ công ghi actor/thời gian vào chính refund; job timeout ghi nguồn System và thời gian.
Các trường audit sau khi lưu không được sửa qua EF. Refund response trong case/admin detail trả audit này.
Điều kiện đủ/amount/reserve balance của refund không đổi. ManualRefundProvider vẫn không chuyển tiền thật
và không được giả Success. Không còn ghi VerificationAdminAction trong luồng refund mới.

## Dữ liệu cũ và migration

Migration `SingleVerificationReportAndRefundAudit`:

- Rename VerificationReports.VerificationId → AssignmentId bằng rename column/index; giữ FK và dữ liệu.
- Giữ nguyên VerificationReportVersions và VerificationAdminAction, không drop/rename. EF giữ mapping
  để không sinh migration xóa bảng, nhưng ngăn ghi vào hai entity lịch sử. Không còn API/service vận hành
  đọc hoặc ghi chúng; chỉ migration đọc để chuyển đổi dữ liệu cũ.
- Trước khi cập nhật nội dung report cũ, lưu đầy đủ hàng gốc vào LegacyOriginalJson. Bảng versions giữ
  đầy đủ mọi revision; không xóa bản gốc hoặc revision nào. Nội dung revision cuối trở thành nội dung
  canonical của report cùng ID gốc, để API mới không trả nhầm kết luận trước revision.
- Dừng migration nếu revision có hiệu lực mới nhất mâu thuẫn outcome của case đã finalized. Không tự
  thay trạng thái/finalizer/khoản tiền của case. Cần kiểm tra và xử lý dữ liệu đó trước phát hành.
- Với case lịch sử chỉ có một report, backfill case FK. Nếu nhiều report lịch sử trên một case, giữ mọi
  hàng và để case FK của report null; LegacyOriginalJson đánh dấu hàng lịch sử. Luồng mới vẫn chặn thêm
  report cho case đã có report. New report bắt buộc có case FK và chịu unique constraint.
- Backfill actor refund từ RefundRequested khi actor xác định duy nhất và action đúng case/payment.
  Nguồn System chỉ suy ra từ lý do timeout chuẩn, case đã hủy tương ứng và thời gian phù hợp, không có
  Admin action. Dữ liệu không đủ hoặc mâu thuẫn giữ LegacyUnknown.
- Downgrade khôi phục nội dung report gốc từ snapshot và sao lưu actor của refund Admin mới vào bảng
  action lịch sử trước khi bỏ các cột audit mới. Không áp downgrade như thao tác phát hành thông thường.

Cần dừng các writer phiên bản cũ khi triển khai, chạy migration trên bản sao dữ liệu và đối chiếu report,
revision, finalized outcome, refund actor/amount/count trước khi áp database thật. Chưa áp migration vào
database ứng dụng trong đợt code này. Link tới revision ID cũ không còn được phục vụ bởi API report mới;
frontend cần dùng ID report canonical và gỡ luồng versions/reviews.

## VerificationNotification và background jobs

Bảng này **đang được dùng cho xác minh sinh viên**: StudentVerificationService ghi thông báo, endpoint
`GET /api/verification-notifications` đọc theo người nhận; StudentVerificationHttpTests kiểm chứng luồng.
Vì vậy giữ nguyên bảng, dữ liệu và API cho chức năng đang dùng, đúng điều kiện người dùng yêu cầu.

Các background job của project verification không còn inject/gọi VerificationNotifications hoặc ghi bảng:

- EvidenceModerationTasks giữ Error/LastErrorCode/LastError/attempts/backoff/lease và log khi cần xử lý.
- VerificationDeadlineTasks giữ overdue → escalation → replacement → timeout/refund bằng timestamp và
  trạng thái nghiệp vụ. OverdueMarkedAt dùng lại cột ReminderSentAt cũ để giữ dữ liệu; nó không có nghĩa
  đã gửi thông báo. Giữ mốc 48 giờ sau meeting, rồi 24 giờ sau khi đánh dấu overdue để escalation.
- VerificationRefundTasks tiếp tục reserve, dispatch/lookup cùng khóa, retry và lưu lỗi/trạng thái; dùng
  logging hiện có để vận hành. Thiếu notification không rollback hoặc treo refund.
- Các job reservation, payment reconciliation và project moderation không cần notification và được giữ.

Không dựng bảng notification thay thế hay mở rộng hệ thống thông báo. Các test mới thực sự drop bảng
VerificationNotification trong database SQLite riêng rồi chạy retry, escalation/thay người và refund,
đồng thời kiểm tra trạng thái/audit được lưu. Test xác minh sinh viên vẫn chạy với bảng thông báo đầy đủ.

## Kết quả kiểm chứng

- Bộ test liên quan D/E và hồi quy A–C: **269 đạt, 0 lỗi, 8 PostgreSQL Skip / 277 test**.
  Bao gồm report một lần, quyền người được giao/người bị thay, chốt case, refund actor System/Admin,
  worker không có bảng notification, xác minh sinh viên, checkout/expiry/PayOS fake, contribution,
  task/report và portfolio. Ba test ràng buộc case và escalation được chạy lại sau chỉnh sửa cuối: đều đạt.
- Hai test PostgreSQL mới kiểm tra submit đồng thời cho cùng assignment và hai assignment cùng case.
  Cùng 6 test PostgreSQL có sẵn, chúng bị Skip do chưa cấu hình UNINET_TEST_POSTGRES; chưa chứng minh
  hành vi tranh chấp trên PostgreSQL thật bằng lượt kiểm thử này.
- Build thành công; EF xác nhận model khớp migration và đã sinh SQL tăng dần từ
  AddVerificationSlotReservations đến SingleVerificationReportAndRefundAudit.
- Chưa áp migration vào database ứng dụng, chưa chạy backfill/rollback trên bản sao dữ liệu lịch sử,
  chưa gọi provider Gemini/Safe Browsing/PayOS thật. Phải đối chiếu dữ liệu và test migration thật trước phát hành.
- Không chọn portrait/ONNX native và hai test lệch contract đã ghi ở A/B (route reconcile cũ, Skill Category
  nullable). Kết quả trên không có nghĩa toàn bộ test suite repository đều pass.
