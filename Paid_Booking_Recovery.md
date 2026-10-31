# Khôi phục lịch đã thanh toán sau khi hết hạn giữ chỗ

API mới: POST /api/admin/project-verifications/{verificationId}/recover-paid-booking
Authorization: Bearer <token Admin đang hoạt động có verification.evidence.review>
Content-Type: application/json

Ví dụ cho case đang xử lý:
```json
{
  "transactionId": "06d28da0-d20f-4454-ad93-9abe0be35d0f",
  "reason": "Đã đối chiếu thanh toán PayOS; kết quả cập nhật chậm do cấu hình, xác nhận lại lịch ban đầu."
}
```
verificationId: 07f5dc38-a65e-4891-ae7e-02b4091fb300

Chỉ gọi sau khi Admin đã kiểm tra giao dịch và đồng ý giữ lại lịch ban đầu.
200 trả ProjectVerificationResponse: bookingStatus=2, bookingConfirmedAt và assignmentEligibleAt có giá trị.
Sau đó có thể POST /api/assignments như bình thường nếu lịch và verificator đáp ứng điều kiện.

Điều kiện: case Pending/chưa chốt/chưa họp/chưa có phân công hoặc báo cáo; lịch trong tương lai; booking Held hoặc Expired; thanh toán Success đang có LATE_PAYMENT_REVIEW_REQUIRED hoặc BOOKING_PAYMENT_REVIEW_REQUIRED; đúng người/gói/reservation; không có thanh toán Pending/Success khác; không có hoàn tiền Pending/Success; bằng chứng cấp 1 còn hợp lệ; còn capacity và verificator rảnh.

Không thay đổi số tiền, PaidAt, ReservationId hay thời hạn giữ cũ. Xác nhận booking và xóa dấu review trên giao dịch được chọn trong cùng transaction Serializable. Lưu dấu vết qua thông báo có transactionId, lý do và SenderUserId của Admin.

409 BOOKING_RECOVERY_NOT_ALLOWED: case/lịch không thể phục hồi (bao gồm đã phục hồi).
409 PAYMENT_NOT_RECOVERABLE: giao dịch không đủ điều kiện hoặc đang hoàn tiền.
409 BOOKING_CAPACITY_UNAVAILABLE: lịch cũ không còn đủ năng lực phục vụ.
Có thể có lỗi LEVEL1_* nếu bằng chứng đã thay đổi. Không bỏ qua điều kiện này bằng sửa database.
Nếu lịch đã qua hoặc không còn chỗ, dùng quy trình xử lý/hoàn tiền hiện có; API này không đổi lịch và không tự chuyển tiền hoàn.

Tài liệu và thay đổi source không tự sửa dữ liệu thật. Cần khởi động lại backend rồi gọi API bằng Swagger.
