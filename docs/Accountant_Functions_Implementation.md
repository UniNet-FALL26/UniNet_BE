# Đặc tả tích hợp chức năng Kế toán và bố cục sidebar

Ngày đối chiếu: 02/11/2026. Backend: UniNet_BE. Vai trò: Accountant = 5.

## 1. Mục tiêu

Triển khai giao diện kế toán dựa trên những API tài chính đã có: quản lý giao dịch, ghi nhận thu nhập, hoàn tiền, xử lý rút tiền, theo dõi số liệu và xuất báo cáo tổng hợp.

Tài liệu này mô tả chức năng backend hiện có và đề xuất cách bố trí giao diện theo ảnh người dùng cung cấp. Các đường dẫn trang frontend là đề xuất để triển khai, không phải route đã xác nhận tồn tại. Không xây thêm nghiệp vụ đối soát hoặc cài đặt tài chính bằng dữ liệu giả để thể hiện như đã được backend hỗ trợ.

## 2. Sidebar bắt buộc theo ảnh

```text
TÀI CHÍNH & KẾ TOÁN
├── Thống kê
├── Yêu cầu rút tiền
└── Cài đặt tài chính
```

Giữ nguyên tiêu đề, nhãn tiếng Việt và thứ tự trên. Không thêm mục cấp một Giao dịch, Hoàn tiền hoặc Đối soát; bố trí các chức năng này trong nội dung các trang như phần bên dưới.

| Thành phần | Yêu cầu hiển thị |
|---|---|
| Tiêu đề nhóm | Icon cơ sở dữ liệu/đồng tiền dạng nét; chữ hoa TÀI CHÍNH & KẾ TOÁN, màu xanh xám; có đường phân cách bên dưới |
| Thống kê | Icon biểu đồ cột; là trang mặc định khi vào khu vực kế toán |
| Yêu cầu rút tiền | Icon thẻ/ví theo mẫu |
| Cài đặt tài chính | Icon bánh răng |
| Mục được chọn | Nền xanh dương đậm như ảnh, chữ và icon trắng, bo góc |
| Mục còn lại | Chữ/icon tối màu trên nền sáng, căn thẳng hàng và khoảng cách đồng đều |
| Trạng thái chọn | Theo URL hiện tại; ảnh mẫu đang chọn Thống kê, khi chuyển trang phải chuyển trạng thái active tương ứng |
| Khả năng truy cập | Dùng liên kết điều hướng, có focus bàn phím và aria-current cho mục đang chọn; không chỉ dùng màu để thể hiện active |

Route frontend đề xuất:

- `/accountant/statistics`: Thống kê.
- `/accountant/withdrawals`: Yêu cầu rút tiền.
- `/accountant/financial-settings`: Cài đặt tài chính.

Nếu frontend đã có route tương đương, giữ route hiện có và ánh xạ ba nhãn trên vào đúng trang. Không đổi route backend theo tên sidebar.

## 3. Phân bổ chức năng vào ba trang

| Trang sidebar | Nội dung bên trong | Mức hỗ trợ |
|---|---|---|
| Thống kê | Tab Tổng quan, Giao dịch thanh toán, Sổ ví & ghi nhận thu nhập, Hoàn tiền | Đã có API |
| Yêu cầu rút tiền | Danh sách, chi tiết trong panel/modal, duyệt/từ chối, lập lệnh chi, xác nhận đã chuyển khoản | Đã có API |
| Cài đặt tài chính | Thông báo chưa có chức năng cấu hình tài chính qua API | Chưa có API cài đặt; không hiển thị form lưu hoạt động giả |

Việc bố trí tab là phương án giao diện trong tài liệu này, không phải khẳng định giao diện đã triển khai. Không đặt ghi có ví hay xác nhận chuyển khoản vào Cài đặt tài chính: đây là nghiệp vụ giao dịch.

## 4. Trang Thống kê — tab Tổng quan

API: `GET /api/admin/finance/summary`, quyền `finance.view`.

Hiển thị các thẻ số liệu:

| Field | Nhãn | Ý nghĩa |
|---|---|---|
| paymentsReceived | Tổng tiền thanh toán thành công | Tổng Transactions có trạng thái Success |
| refundsPaid | Tổng tiền đã hoàn | Tổng RefundTransactions có trạng thái Success |
| withdrawalsPaid | Tổng tiền đã chi rút ví | Tổng yêu cầu rút có trạng thái Paid |
| walletIncome | Tổng thu nhập ghi vào ví | Tổng bút toán ví có amount > 0 |
| pendingWithdrawals | Tổng tiền rút đang chờ xử lý | Tổng tiền ở Requested, Approved, Ordered |
| refundsAwaitingReview | Yêu cầu hoàn tiền chờ duyệt | Số lượng hoàn tiền có approval Pending |

Không gọi walletIncome là doanh thu hệ thống. Không cộng/trừ các chỉ tiêu để gọi là lợi nhuận khi backend chưa định nghĩa báo cáo đó.

Nút Xuất báo cáo CSV: `GET /api/admin/finance/report.csv`, quyền `finance.report.export` và `finance.view` theo kiểm tra hiện có. Tải file `finance-summary.csv`. CSV gồm cột Metric,VND và 5 chỉ tiêu tiền ở trên; không bao gồm refundsAwaitingReview.

Số liệu hiện là tổng hợp toàn bộ dữ liệu, API không có from/to. Chưa triển khai bộ lọc ngày/tháng hay biểu đồ xu hướng dựa trên dữ liệu không đầy đủ. Có loading, empty/error và nút tải lại.

## 5. Trang Thống kê — tab Giao dịch thanh toán

API: `GET /api/admin/finance/payments?page=1`, quyền `finance.view`.

Response phân trang: page, pageSize, total, items. Backend dùng 30 bản ghi/trang, sắp xếp mới nhất trước.

FinancePayment gồm id, accountId, email, fullName, packageName, amount, status, paymentMethod, paymentRef, createdAt, reservedRefund.

Bảng hiển thị mã giao dịch, người thanh toán, gói dịch vụ, số tiền, trạng thái, phương thức, mã tham chiếu, ngày tạo và số tiền đang được giữ cho hoàn tiền. Có thể mở chi tiết từ bản ghi đã tải; hiện không có endpoint finance riêng để lấy chi tiết payment theo ID.

Cho phép mở form Tạo yêu cầu hoàn tiền từ giao dịch thành công, sau đó chuyển sang tab Hoàn tiền. Backend kiểm tra số tiền còn có thể hoàn. Không triển khai sửa/xóa giao dịch thanh toán hoặc tự đổi trạng thái thành công.

API hiện không nhận bộ lọc trạng thái/ngày hay search. Không trình bày lọc trên một trang dữ liệu như kết quả lọc toàn hệ thống.

## 6. Trang Thống kê — tab Sổ ví & ghi nhận thu nhập

### 6.1. Sổ biến động ví

`GET /api/admin/finance/ledger?page=1`, quyền `finance.view`.

Response: page, pageSize, total, items; 30 bản ghi/trang. Item gồm id, accountId, amount, sourceReference, reason, actorAccountId, withdrawalId, createdAt. Hiển thị rõ bút toán ghi có và ghi nợ. Đây là sổ ví, không gọi là sổ cái kế toán đầy đủ của doanh nghiệp.

### 6.2. Ghi nhận thu nhập cho Assessor

- Tìm người nhận: `GET /api/admin/finance/recipients?search={text}`.
- Search tối thiểu 3 ký tự; tối đa 20 kết quả; chỉ trả Assessor active.
- Response item: accountId, email, fullName.
- Ghi có: `POST /api/admin/finance/wallet/credits`.
- Cả hai yêu cầu quyền `finance.wallet.credit`.

```json
{
  "accountId": "<account-uuid-cua-assessor>",
  "amount": "500000",
  "sourceReference": "INCOME-2026-001",
  "reason": "Ghi nhận thu nhập thẩm định theo chứng từ"
}
```

Response WalletEntryResponse: id, amount, sourceReference, reason, createdAt.

Amount là VND nguyên dương, tối đa 999999999999999999. sourceReference bắt buộc, tối đa 150 ký tự, chuẩn hóa hoa; không được bắt đầu bằng WITHDRAWAL:. reason bắt buộc, tối đa 2.000 ký tự. Cùng người nhận/sourceReference và cùng nội dung trả lại bút toán cũ; khác nội dung trả IDEMPOTENCY_CONFLICT. Không sửa mã tham chiếu chỉ để vượt qua thông báo trùng.

Không giả định nộp báo cáo thẩm định tự động ghi có ví. Luồng hiện có yêu cầu ghi nhận thu nhập qua API tài chính.

## 7. Trang Thống kê — tab Hoàn tiền

| API | Quyền | Chức năng |
|---|---|---|
| GET `/api/admin/finance/refunds?page=1` | finance.view | Danh sách, 30 mục/trang |
| POST `/api/admin/finance/refunds` | finance.refund.create | Tạo yêu cầu |
| POST `/api/admin/finance/refunds/{id}/review` | finance.refund.review | Duyệt hoặc từ chối |
| POST `/api/admin/finance/refunds/{id}/confirm` | finance.payout.confirm | Xác nhận đã hoàn tiền thủ công |

Body tạo:

```json
{
  "requestKey": "<uuid-khong-rong>",
  "transactionId": "<payment-uuid>",
  "amount": "200000",
  "reason": "Lý do hoàn tiền"
}
```

Body review: `{ "approve": true, "reason": "Đã kiểm tra điều kiện hoàn" }`; dùng approve=false để từ chối.

Body confirm: `{ "payingAccount": "TAI-KHOAN-CHI", "bankReference": "MA-GIAO-DICH-NGAN-HANG" }`.

FinanceRefund response gồm id, transactionId, amount, status, approval, reason, requestedByUserId, requestedAt, reviewedByAccountId, reviewedAt, reviewReason, refundRef, failureReason. Không nhầm requestedByUserId (profile ID) với account ID để so sánh người tự duyệt; backend là nguồn kiểm tra cuối cùng.

Luồng: tạo yêu cầu → một nhân sự có quyền khác duyệt → thực hiện chuyển tiền ngoài hệ thống → xác nhận bằng tài khoản chi và mã giao dịch ngân hàng → trạng thái thành công.

Các điều kiện bắt buộc:

- Giao dịch gốc phải thành công; số tiền không vượt phần chưa bị giữ/đã dùng cho hoàn tiền.
- Không cho người tạo tự duyệt yêu cầu. Không tự suy diễn cấm người tạo confirm: backend hiện kiểm tra tách người ở bước review.
- Review chỉ hợp lệ khi approval Pending, payment status Pending và chưa dispatch.
- Confirm yêu cầu approval Approved hoặc NotRequired, trạng thái Pending, chưa dispatch và giao dịch gốc thành công.
- Confirm là ghi nhận một chuyển khoản đã thực hiện; không tự chuyển tiền ngân hàng.
- Cặp payingAccount/bankReference không được dùng trùng với hoàn tiền hoặc chi rút ví khác.
- Giữ nguyên requestKey và payload khi retry tạo yêu cầu; cùng key khác nội dung bị từ chối.
- Chỉ bật nút khi đủ điều kiện theo dữ liệu có sẵn; backend vẫn kiểm tra lại. Nếu response không đủ trường để biết dispatch, xử lý lỗi trạng thái khi server trả về.

Sau thao tác, tải lại danh sách và Tổng quan. Không có API chỉnh sửa/xóa yêu cầu hoàn tiền trong controller hiện tại.

## 8. Trang Yêu cầu rút tiền

### 8.1. Danh sách và thông tin chi tiết

`GET /api/admin/finance/withdrawals?page=1`, quyền `finance.view`, response page/pageSize/total/items, 30 bản ghi/trang.

WithdrawalResponse gồm id, accountId, amount, bankName, bankAccountNumber, bankAccountHolder, state, createdAt, reviewedBy, reviewedAt, reviewReason, orderedBy, orderedAt, confirmedBy, confirmedAt, payingAccount, bankReference, rejectedBy, rejectedAt, rejectionReason.

Bảng hiển thị mã yêu cầu, tài khoản người nhận, số tiền, ngân hàng, số tài khoản, chủ tài khoản, ngày tạo và trạng thái. Panel chi tiết hiển thị lịch sử người/thời điểm duyệt, lập lệnh, xác nhận hoặc từ chối từ các field trên. Không có endpoint detail riêng; dùng dữ liệu danh sách và tải lại khi cần. Response không có tên/email người nhận; không giả định tồn tại API tra cứu người dùng mà Accountant được quyền gọi.

### 8.2. Hành động theo trạng thái

| Trạng thái | Giá trị | Hành động được backend hỗ trợ |
|---|---|---|
| Requested | 0 | Duyệt hoặc từ chối |
| Approved | 1 | Lập lệnh chi hoặc từ chối |
| Ordered | 2 | Xác nhận đã chuyển khoản |
| Paid | 3 | Xem kết quả và mã ngân hàng |
| Rejected | 4 | Xem lý do từ chối |
| Cancelled | 5 | Xem yêu cầu đã được chủ ví hủy |

| Method | Endpoint | Quyền | Body |
|---|---|---|---|
| POST | `/api/admin/finance/withdrawals/{id}/review` | finance.withdrawal.review | approve, reason |
| POST | `/api/admin/finance/withdrawals/{id}/order` | finance.payout.create | Không cần body nghiệp vụ |
| POST | `/api/admin/finance/withdrawals/{id}/confirm` | finance.payout.confirm | payingAccount, bankReference |

Review body: `{ "approve": true, "reason": "Hồ sơ hợp lệ" }`. Lý do bắt buộc, tối đa 2.000 ký tự. Confirm body giống xác nhận hoàn tiền; tài khoản chi và tham chiếu bắt buộc, tối đa 150 ký tự mỗi trường, được chuẩn hóa hoa.

Luồng chính: Requested → Approved → Ordered → chuyển khoản ngoài hệ thống → Paid. Requested/Approved có thể bị từ chối; không cung cấp nút từ chối ở Ordered. Gọi order lại ở Ordered được chấp nhận. Confirm Paid chỉ được coi là retry thành công khi chứng từ khớp lần trước.

Requested/Approved/Ordered giữ tiền reserved; Paid tạo ghi nợ ví đúng một lần. Xác nhận yêu cầu đã có chuyển khoản thật, không đánh dấu Paid ngay khi bấm lập lệnh. Lưu giữ trạng thái duyệt cũ khi từ chối sau duyệt; hiển thị rejectionReason riêng.

Chủ ví là Assessor active; Accountant không dùng `/api/wallet` làm ví cá nhân và không tạo yêu cầu rút thay người nhận qua các endpoint finance hiện tại. Hủy yêu cầu là thao tác của chủ ví khi Requested, không phải hành động kế toán trong trang này.

## 9. Trang Cài đặt tài chính

Giữ mục này trong sidebar đúng ảnh. Hiện chưa thấy API finance để đọc/lưu cấu hình tài chính trong mã nguồn đã đối chiếu.

Ở phạm vi tích hợp các chức năng đã có:

- Hiển thị trang với tiêu đề Cài đặt tài chính và trạng thái trống: "Chưa có cấu hình tài chính khả dụng."
- Có thể có phần hướng dẫn quy trình chỉ đọc; không trình bày dữ liệu cấu hình chưa được backend cung cấp.
- Không có nút Lưu giả hoặc thông báo cập nhật thành công khi chưa gọi API lưu thực.
- payingAccount nhập trong từng lần xác nhận giao dịch là chứng từ nghiệp vụ; không phải cấu hình tài khoản chi mặc định.
- Cấu hình PayOS hiện thuộc môi trường/backend, không đưa khóa bí mật hoặc cấu hình môi trường vào giao diện kế toán.

Các chức năng như tài khoản chi mặc định, hạn mức rút, phí, lịch chi, ngưỡng duyệt là phạm vi bổ sung tương lai, cần đặc tả và API riêng trước khi tích hợp. Không liệt kê chúng là chức năng đã hoàn thành.

## 10. Đối soát — phạm vi đã có và chưa có

Đã có backend tự đối soát thanh toán PayOS đang Pending thông qua background worker và nhận webhook có kiểm tra chữ ký. Luồng nền kiểm tra trạng thái nhà cung cấp và kiểm tra đơn, số tiền, tiền tệ, liên kết thanh toán trước khi cập nhật dữ liệu. Việc xác nhận chi thủ công có chặn trùng chứng từ ngân hàng.

Chưa có API chuyên biệt cho Accountant để nhập sao kê, ghép giao dịch, mở danh sách chênh lệch, xử lý chênh lệch, chốt phiên đối soát hoặc chủ động chạy đối soát theo lô. Không thêm nút Chạy đối soát hoặc Chốt đối soát khi chưa có backend hỗ trợ. Trạng thái thanh toán và mã tham chiếu hiện được xem trong tab Giao dịch thanh toán.

Không gọi việc chỉ nhập bankReference là hệ thống đối soát ngân hàng đầy đủ.

## 11. Quy tắc tích hợp chung và phân quyền

- Base API finance: `/api/admin/finance`. Dù có chữ admin, controller cho phép cả Admin và Accountant.
- Gửi Authorization Bearer JWT; API nghiệp vụ dùng accountId từ phiên đăng nhập.
- Quyền mặc định Accountant: finance.view, finance.wallet.credit, finance.refund.create, finance.refund.review, finance.withdrawal.review, finance.payout.create, finance.payout.confirm, finance.report.export. Admin có thể thu hồi từng quyền; client xử lý 403 và không mặc định mọi Accountant có đủ nút thao tác.
- DTO JSON camelCase; UUID cho ID. Thời gian ISO 8601, hiển thị theo múi giờ người dùng.
- Các response tiền từ finance được serialize thành chuỗi decimal. Giữ chính xác dưới dạng string/decimal phù hợp; không dùng JavaScript Number cho tiền lớn. Request có thể gửi amount dạng chuỗi số theo contract ASP.NET hiện hành.
- Finance GET không được cache dữ liệu nhạy cảm theo cách làm lộ phiên người dùng khác. Khi đổi tài khoản, xóa state/cache tài chính của phiên cũ.
- Không tự thêm query ngày/trạng thái/search vào API không hỗ trợ. Chỉ recipients hiện có search; các danh sách finance nhận page.
- Disable nút khi đang gửi; với POST không có khóa retry rõ ràng, tải lại dữ liệu trước khi gửi lại khi không biết kết quả.
- 400: giữ form và hiển thị validation; 401: xử lý phiên; 403: thiếu quyền; 404: đối tượng không còn; 409: tải lại vì xung đột/trạng thái thay đổi.
- Lỗi chính gồm INVALID_AMOUNT, INVALID_INPUT, INVALID_REQUEST_KEY, IDEMPOTENCY_CONFLICT, INVALID_REFUND_AMOUNT, SECOND_REVIEWER_REQUIRED, INVALID_STATE, DUPLICATE_TRANSFER, FINANCE_CONFLICT, NOT_FOUND. Không giả định mọi lỗi framework có cùng envelope với `{code,message}`.
- Dùng modal xác nhận cho từ chối, ghi có và xác nhận chuyển khoản; hiển thị số tiền, người nhận/mã yêu cầu và chứng từ trước khi gửi.

## 12. Checklist triển khai và nghiệm thu

- [ ] Sidebar đúng tiêu đề, 3 nhãn, thứ tự, icon và trạng thái active theo ảnh.
- [ ] Các chức năng giao dịch/hoàn tiền nằm trong tab Thống kê, không thêm mục sidebar cấp một.
- [ ] Tổng quan hiển thị đúng ý nghĩa 6 chỉ tiêu; không nhầm thu nhập ví thành doanh thu/lợi nhuận.
- [ ] CSV tải đúng file và số liệu; không tuyên bố là báo cáo theo kỳ.
- [ ] Danh sách phân trang đúng 30 bản ghi; không lọc giả trên dữ liệu chưa tải hết.
- [ ] Ghi có đúng Assessor active; trùng sourceReference được xử lý đúng.
- [ ] Hoàn tiền không vượt số tiền khả dụng và không cho tự duyệt.
- [ ] Retry cùng requestKey không tạo trùng yêu cầu hoàn tiền.
- [ ] Rút tiền chỉ thực hiện hành động hợp lệ theo state và quyền.
- [ ] Không xác nhận Paid trước khi chuyển tiền thực; không ghi nợ hai lần khi retry.
- [ ] Một chứng từ không được dùng cho cả hai khoản chi khác nhau.
- [ ] Cài đặt tài chính có trang rõ ràng nhưng không có form/API lưu giả.
- [ ] Không có chức năng đối soát sao kê hoặc chốt kỳ giả định đã làm.
- [ ] Role khác và Accountant bị thu hồi quyền không thực hiện được API tương ứng.
- [ ] Loading/lỗi/empty/retry, hủy request cũ và bảo toàn dữ liệu form hoạt động đúng.
- [ ] Kiểm tra trên desktop/mobile và bàn phím; giữ sidebar dễ đọc như mẫu.

## 13. Nguồn mã đối chiếu

- `UniNet.API/Controllers/FinanceController.cs`: endpoint, response DTO và xuất CSV.
- `UniNet.Application/Services/FinanceService.cs`: ví, ghi có, rút tiền, trạng thái và tính chính xác tiền.
- `UniNet.Application/Services/FinanceRefunds.cs`: thanh toán, ledger, summary, hoàn tiền và tìm người nhận.
- `UniNet.Application/Services/NotificationPermissionPolicy.cs`: quyền mặc định Accountant/Admin và kiểm tra quyền.
- `UniNet.Application/Services/TransactionService.cs`: đối soát thanh toán nền và kiểm tra dữ liệu nhà cung cấp.
- `UniNet.API/Controllers/TransactionController.cs`: webhook thanh toán; API payment của Student không thay cho finance API của Accountant.
- `UniNet.API/Services/FinanceJsonAttribute.cs`: định dạng tiền dạng chuỗi trong response.

Tài liệu dựa trên rà soát mã nguồn, không khẳng định các màn hình đã được xây dựng hoặc các checklist đã được kiểm thử thực tế trong lần soạn này.
