# API trạng thái project và các tác vụ tự động

Phần kiểm duyệt evidence cấp 1 khi Active → Completed được bổ sung trong [project-evidence-moderation.md](project-evidence-moderation.md). Đây là tác vụ riêng với completeness pre-check và moderation đầu vào mô tả bên dưới.

## PATCH /api/projects/status

Body: `{"projectId":"<guid>","status":2}` (`2` = Completed).
Chỉ Student đang Active và là creator của project được phép gọi. Chỉ chấp nhận
project hiện tại đang Active chuyển sang Completed; mọi trạng thái đích khác bị từ
chối. Cập nhật có điều kiện trong một câu lệnh database để tránh ghi đè trạng thái
đã đổi bởi một request khác. Trả về `projectId`, `status`, `updatedAt`.

## BackgroundService, không có bảng Job

`VerificationBackgroundWorker` chạy các vòng xử lý độc lập. Mặc định kiểm tra mỗi
60 giây, thực thi khi mốc nghiệp vụ đã đến; không đảm bảo chạy đúng một giây cụ thể.
Mỗi vòng có PostgreSQL session advisory lock riêng, chỉ một instance xử lý cùng loại
tác vụ tại một thời điểm. Khóa được giữ trên cùng kết nối cho đến khi kết thúc vòng,
và tự được database giải phóng khi kết nối bị mất. Những thay đổi nghiệp vụ dùng
transaction Serializable, đọc lại điều kiện trước khi ghi. Xung đột được kiểm tra lại
ở vòng kế tiếp. Không giữ transaction database trong lúc gọi AI/payment/refund provider.

| Tác vụ | Điều kiện và hành động |
| --- | --- |
| Verification AI review/retry | Evidence mới sẽ được pre-check. Retry lỗi kết nối, timeout, HTTP 408/429/5xx với backoff 1/2/4/8 phút, tối đa 5 lần gọi cho mỗi phiên bản evidence. Lỗi cấu hình/quyền truy cập không retry tự động; báo admin. Không ghi đè kết quả lên evidence đã thay đổi. |
| Project moderation AI retry | Tái thử lỗi provider tạm thời trong luồng moderation hiện có, tối đa 5 lần retry nền, khoảng cách ít nhất 5 phút. Giữ nguyên quy tắc approval hiện tại. Khôi phục attempt Processing bị bỏ dở trên 10 phút. |
| Payment reconciliation | Kiểm tra các verification payment Pending thuộc provider đã cấu hình, ít nhất 5 phút sau lần cập nhật trước. Dùng chung kiểm tra order, amount, currency, link ID và xử lý webhook; không tin trạng thái frontend. |
| Assignment timeout | Sau 24 giờ từ payment Success đầu tiên (`PaidAt`, fallback `CreatedAt` cho dữ liệu cũ) mà chưa có assignment đang hoạt động hoặc hoàn thành: hủy verification và khởi tạo hoàn toàn bộ số tiền thực tế đã thu. Không hoàn tiền cho yêu cầu chưa thanh toán. |
| Report deadline | Sau 48 giờ từ `MeetingEndAt`, assignment ban đầu chưa có report: đánh dấu Overdue, lưu `ReminderSentAt` và gửi một thông báo trong hệ thống cho verificator. |
| Report escalation | Sau 24 giờ từ reminder vẫn chưa có report: báo admin và tìm verificator Active, không là requester/creator/thành viên dự án, chưa từng được phân công vào verification này và hiện không có assignment Active/Overdue khác. Lưu quan hệ thay thế và thông báo cho người mới. |
| Replacement timeout | Sau 48 giờ từ thời điểm thực sự phân công người thay thế mà chưa có report: hủy verification/assignment, khởi tạo hoàn toàn bộ số dư đã thanh toán. Không dùng lại deadline của meeting cũ để nhắc người thay thế ngay lập tức. |
| Refund reconciliation | Lưu dấu dispatch trước khi gọi provider. Sau lần gọi đầu tiên, chỉ lookup cùng key = RefundTransaction.Id; timeout/unknown giữ Pending, không gửi một refund mới. Backoff tăng dần, tối đa 60 phút. |

Nếu không có người thay thế hợp lệ hoặc không có admin Active, hệ thống gửi cảnh báo
và tiếp tục tìm ở vòng sau. Không tạo assignment giả hoặc mượn danh người dùng khác.
`AdminId` trên assignment tự động dùng admin Active làm người chịu trách nhiệm vận hành;
`ReplacementReason` ghi rõ đây là thay thế tự động của hệ thống.

`VerificationNotification` là bảng thông báo nghiệp vụ, không phải bảng Job.
GET `/api/verification-notifications?page=1&pageSize=20` chỉ trả thông báo của tài khoản
Admin/Verificator đang Active. Có unique key theo sự kiện và người nhận để không gửi lặp.
Thông báo hiện nằm trong hệ thống; chưa có kênh email/SMS/push.

Không có HTTP endpoint chạy job/khởi tạo refund cho người dùng thông thường.
Đã bỏ route frontend cũ `POST /api/payments/{transactionId}/reconcile`; frontend đọc trạng thái
đã lưu bằng endpoint GET hiện có. Refund thủ công của admin vẫn đi qua quyền Admin và audit hiện có.

## Hoàn tiền và giới hạn tích hợp hiện tại

- Transaction thu tiền gốc luôn giữ Success. Các khoản hoàn nằm riêng trong RefundTransactions.
- Tính theo transaction.Amount đã Success, không dùng giá package hiện tại.
- Khấu trừ các khoản Pending/Success đã hoàn/đang hoàn. Refund tự động Failed vẫn giữ dấu
  để không tự tạo yêu cầu thay thế khi job chạy lại; cần người vận hành xử lý.
- Nếu webhook đến muộn và xác nhận thêm một khoản thu cho verification đã timeout,
  tác vụ sẽ tạo khoản hoàn cho khoản thu mới đó.
- Nếu đã có report, các tác vụ timeout không tự hoàn tiền vì report Failed.
- IRefundProvider yêu cầu create và lookup theo cùng request key; provider adapter phải
  trả Pending khi chưa rõ kết quả hoặc không tìm được yêu cầu sau timeout.
- Adapter mặc định là ManualRefundProvider: **chưa gửi tiền và chưa tự xác nhận hoàn tiền**.
  Tích hợp checkout payOS hiện tại không cung cấp adapter refund/lookup. Worker giữ Pending
  và báo admin cần xử lý. Không dùng thao tác hủy link thanh toán thay cho hoàn tiền.
  Cần adapter provider có khả năng hoàn tiền và đối soát được xác thực trước khi bật chuyển tiền tự động.

AI pre-check chỉ phân tích metadata evidence (tiêu đề, mô tả, URL), không tải hay đọc
tài liệu từ URL, không kết luận Verified/Failed và không kích hoạt refund. Điểm AI
không phải đánh giá chất lượng nội dung tài liệu.

## Triển khai

Áp dụng migration `AddVerificationBackgroundState` trước khi bật worker:

```powershell
dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.API
```

Không tự chạy migration trên database trong thay đổi này. Có thể tắt worker khi
triển khai migration qua biến môi trường `VerificationWorker__Enabled=false`.
`VerificationWorker__PollSeconds` điều chỉnh nhịp kiểm tra (10–3600 giây), mặc định 60.
Các thiết lập vận hành này không thay đổi các mốc nghiệp vụ 24/48 giờ.

## Hướng xử lý hai giá trị trong appsettings (chưa thay đổi hành vi)

Hiện `ProjectService` lấy giới hạn dự án từ `Projects:MaxOngoingProjectsPerStudent`
(fallback 3); `ProjectModerationService` tự approve theo `ProjectModeration:AutoApproveConfidence`
(hiện 0.9). Chuyển hai số này sang database đơn thuần không giải quyết vấn đề thiếu
căn cứ nghiệp vụ mà hội đồng đang phản biện.

Đề xuất ưu tiên:

1. **Giới hạn project:** nếu không có quy định chính thức, bỏ giới hạn 3 dự án thay vì
   đổi nơi lưu số 3. Nếu có chính sách thật của trường/gói dịch vụ, lấy quota từ chính
   sách đã được phê duyệt và có hiệu lực cho người dùng. Package hiện tại chưa có quota
   hay entitlement nên không thể suy diễn giới hạn từ bảng đó.
2. **Auto approval:** AI chỉ gợi ý, mọi project chuyển sang ReviewRequired; Moderator
   quyết định Approved/Rejected kèm audit. Không tự chuyển Active chỉ vì model trả 0.9.
   Confidence của LLM không tự chứng minh độ chính xác đã được hiệu chuẩn.
3. Nếu sản phẩm thực sự cần chính sách tự động có thể điều chỉnh: thiết kế chính sách
   nghiệp vụ có version, người duyệt, ngày hiệu lực, phạm vi áp dụng, lý do thay đổi;
   lưu version chính sách được dùng trên mỗi quyết định. Không cho Admin chỉnh số tùy ý
   mà thiếu lịch sử/căn cứ kiểm định.
4. Appsettings giữ cấu hình kỹ thuật như URL provider, timeout, log, nhịp worker và
   tham chiếu secret; không dùng làm nguồn quyết định nghiệp vụ không có căn cứ.

**Hai cấu hình đang bị phản biện vẫn giữ nguyên trong lần triển khai này**, theo yêu
cầu nghiên cứu hướng đi trước. Muốn loại bỏ hoàn toàn auto approval cần thay cả nhánh
quyết định trong service, không chỉ xóa key khỏi appsettings.
