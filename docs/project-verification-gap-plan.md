# Kế hoạch bổ sung luồng xác thực dự án

Ngày lập: 17/10/2026. Trạng thái: kế hoạch, chưa triển khai code.

## 1. Mục tiêu và phạm vi đã thống nhất

Hoàn thiện luồng: người tạo đánh dấu dự án Completed → hệ thống kiểm duyệt evidence cấp 1 → nếu đạt, sinh viên có thể chọn xác thực cấp 2 → đặt lịch → thanh toán → Admin phân công verificator → nộp report và chốt kết quả sau cuộc họp.

- Giữ nguyên `ProjectModerationService`: kiểm duyệt đầu vào khi tạo dự án, độc lập với kiểm duyệt evidence sau khi hoàn tất.
- Dùng dữ liệu thật từ `Project → ProjectTask → TaskReport → TaskEvidence`; không yêu cầu leader nhập lại một bộ evidence để thay thế evidence sinh viên đã nộp.
- Cấp 1 không phụ thuộc việc mua package hay đăng ký cấp 2. Cấp 2 là tùy chọn.
- Khi Admin assign, verificator bắt buộc nhận; không bổ sung accept/reject case.
- Giữ report dữ liệu qua API và cơ chế phiên bản/audit hiện có. Không làm xuất PDF/Word.
- Không triển khai phòng họp, video/audio, link tham gia, điểm danh hoặc tích hợp nhà cung cấp họp. Chỉ giữ các mốc bắt đầu/kết thúc đang có làm điều kiện của report.
- Đặt lịch và giữ slot thuộc phạm vi kế hoạch vì cần hoàn thiện thứ tự đặt lịch → thanh toán; không phải triển khai cuộc họp.

## 2. Hiện trạng và khoảng trống

| Hạng mục | Hiện trạng | Thay đổi cần làm |
| --- | --- | --- |
| Hoàn tất dự án | Có `PATCH /api/projects/status`, kiểm tra creator và Active → Completed | Ghi nhận lượt kiểm duyệt cấp 1 cùng thao tác hoàn tất |
| Nguồn evidence | Verify dùng JSON riêng trong `ProjectVerificationEvidence` | Tổng hợp task report/evidence, lưu nguồn và phiên bản đã kiểm tra |
| AI | Chỉ chấm độ đầy đủ metadata | Kiểm duyệt nội dung cấm và link không an toàn, trả kết quả có cấu trúc |
| Trạng thái cấp 1 | Chưa có kết quả độc lập | Lưu trạng thái kiểm duyệt, lý do và độ mới của kết quả |
| Chặn cấp 2 | Chưa yêu cầu AI đạt | Kiểm tra cấp 1 tại tạo yêu cầu, đặt lịch, thanh toán và phân công |
| Đặt lịch/thanh toán | Phải trả tiền trước khi xem slot/đặt lịch | Giữ slot trước, tạo thanh toán sau, xác nhận slot khi thanh toán hợp lệ |
| Phân công | Có kiểm tra lịch/người phù hợp | Kiểm tra lại thanh toán và cấp 1, thông báo người được giao |
| Report | Có nộp, phân quyền, phiên bản và chốt kết quả | Giữ hành vi cốt lõi; bổ sung quyền thành viên nếu áp dụng chính sách ở mục 3 |

## 3. Quy tắc thiết kế đề xuất cần chốt khi triển khai

Các mục dưới đây là đề xuất, không phải yêu cầu đã được xác nhận hoặc chức năng đã có.

1. **Chọn evidence:** lấy phiên bản TaskReport mới nhất của từng sinh viên trong từng task, kèm Content và toàn bộ TaskEvidence của phiên bản đó. Không chỉ lấy report Approved vì có thể bỏ sót nội dung đang chờ duyệt; giữ trạng thái report trong snapshot để truy vết. Task/report đã hủy hoặc bị xóa cần chính sách rõ ràng, không âm thầm bỏ qua.
2. **Không có evidence:** trả `NeedsEvidence`, không tự cho đạt. Không tự bổ sung điều kiện tất cả task phải Completed nếu nghiệp vụ chưa yêu cầu.
3. **Phạm vi đọc file:** phân biệt kiểm tra văn bản/URL và kiểm tra nội dung bên trong file. Chốt định dạng được hỗ trợ, giới hạn dung lượng và cách xử lý file không đọc được; không ghi nhận toàn bộ evidence an toàn khi còn mục chưa kiểm tra.
4. **Quyền đọc report:** đề xuất creator và thành viên Active của đúng dự án được xem report hiện hành. Giữ lịch sử phiên bản cho Admin và đúng verificator như hiện tại. Nếu chỉ creator được xem là chủ đích, bỏ hạng mục mở quyền này.
5. **Giữ slot:** thời hạn là cấu hình được thống nhất; không tự ấn định số phút trong code. Đồng bộ hạn thanh toán với hạn giữ slot.
6. **Evidence thay đổi:** đề xuất khóa thay đổi nguồn evidence trong một chu kỳ cấp 2 đang thanh toán/đã phân công; trước thời điểm khóa, thay đổi nguồn làm kết quả cấp 1 cũ hết hiệu lực và cần kiểm tra lại. Chốt cơ chế sửa sai có audit để không khóa vĩnh viễn dữ liệu bị từ chối.

## 4. Kế hoạch thực hiện

### Giai đoạn 1 — Trạng thái cấp 1 và snapshot nguồn evidence

- [ ] Thiết kế trạng thái riêng cho cấp 1, ví dụ: Pending, Processing, Passed, Rejected, NeedsEvidence, Error; biểu diễn rõ kết quả cũ hết hiệu lực.
- [ ] Tách kết quả cấp 1 khỏi `ProjectVerificationStatus` đang dùng cho case cấp 2. Không dùng điểm completeness làm kết quả kiểm duyệt.
- [ ] Lưu lượt kiểm duyệt theo ProjectId: phiên bản/hash nguồn, thời điểm, phiên bản chính sách, kết quả tổng hợp, lỗi provider và lịch sử thử lại.
- [ ] Lưu snapshot bất biến: ProjectTaskId, TaskReportId, Version, SubmittedById, Content, TaskEvidenceId, URL/metadata và dấu vết nội dung đã kiểm tra khi có đọc file.
- [ ] Tạo lượt Pending trong cùng transaction đổi Active → Completed; gọi provider ngoài transaction qua worker.
- [ ] Bảo đảm idempotency khi gọi lại API/worker hoặc nhiều instance chạy đồng thời; có phục hồi Processing bị bỏ dở.
- [ ] Tạo migration và mapping; giữ nguyên luồng moderation khi tạo dự án.

**Nghiệm thu:** hoàn tất dự án không cần package; sinh ra đúng một lượt kiểm duyệt cho cùng phiên bản nguồn; snapshot truy được evidence của từng sinh viên và không bị thay bằng link nhập riêng.

### Giai đoạn 2 — Kiểm duyệt nội dung và an toàn URL

- [ ] Xây dựng service tổng hợp đầu vào từ snapshot, bao gồm nội dung report và các URL xuất hiện trong nội dung/evidence.
- [ ] Tách contract kiểm duyệt evidence khỏi contract completeness hiện có và khỏi luồng `ProjectModerationService`.
- [ ] Quản lý chính sách từ khóa cấm có phiên bản; xác định xử lý dấu, hoa/thường, ngữ cảnh và lý do vi phạm.
- [ ] Tích hợp cơ chế kiểm tra URL phù hợp, có bằng chứng kết quả; không coi HTTPS hoặc suy đoán của AI là xác nhận link an toàn.
- [ ] Nếu tải file/URL: kiểm soát đích truy cập, redirect, mạng nội bộ, timeout, dung lượng và loại file; không thực thi nội dung tải về.
- [ ] Khi cần đọc file: bổ sung trích xuất văn bản/OCR cho phạm vi đã chốt; ghi rõ mục không đọc được hoặc không hỗ trợ.
- [ ] Trả kết quả từng mục: nguồn evidence, loại kiểm tra, trạng thái, lý do, thời điểm; tổng hợp Passed chỉ khi mọi kiểm tra bắt buộc đã đạt.
- [ ] Provider lỗi/timeout hoặc kết quả không hợp lệ phải là Error/chờ xử lý, không tự Passed hay đánh đồng với vi phạm.
- [ ] Retry lỗi tạm thời có giới hạn/backoff; báo Admin khi hết lượt. Chỉ ghi kết quả nếu snapshot vẫn là phiên bản cần kiểm tra.
- [ ] Thiết kế luồng bổ sung/sửa evidence và kiểm tra lại, lưu lịch sử, không ghi đè kết quả cũ.

**Nghiệm thu:** có ca vi phạm từ khóa, link không an toàn, file không đọc được, provider lỗi và thay đổi nguồn khi AI đang chạy; mọi kết quả đều truy vết được về task report/evidence.

### Giai đoạn 3 — API cấp 1 và điều kiện vào cấp 2

- [ ] Bổ sung API đọc kết quả cấp 1 theo project; endpoint đề xuất `GET /api/projects/{projectId}/evidence-moderation`.
- [ ] Bổ sung API yêu cầu kiểm tra lại sau khi sửa nguồn theo chính sách; endpoint đề xuất `POST /api/projects/{projectId}/evidence-moderation/resubmit`.
- [ ] Phân quyền creator/thành viên và Admin; không cho client tự gửi verdict hoặc tự đặt Passed.
- [ ] Khi tạo yêu cầu cấp 2: bắt buộc Completed, cấp 1 Passed, phiên bản nguồn hiện hành; chọn package ở bước này.
- [ ] Gắn case cấp 2 với lượt cấp 1/snapshot cụ thể. Kiểm tra lại các điều kiện tại đặt lịch, tạo thanh toán và phân công.
- [ ] Ngừng dùng evidence JSON nộp riêng làm nguồn kiểm duyệt chính. Giữ dữ liệu cũ phục vụ audit; nếu cho đính kèm bổ sung phải phân biệt rõ và không thay thế TaskEvidence.
- [ ] Định nghĩa mã lỗi nghiệp vụ ổn định cho pending, rejected, stale, missing evidence và lỗi provider.

**Nghiệm thu:** không thể vượt cấp bằng gọi API trực tiếp; dự án chỉ đạt cấp 1 vẫn có kết quả hiển thị dù không đăng ký cấp 2.

### Giai đoạn 4 — Đặt lịch trước, thanh toán sau

- [ ] Cho xem slot khi đủ điều kiện cấp 1 mà chưa thanh toán.
- [ ] Đặt lịch tạo reservation có hạn, chưa được phân công; phân biệt rõ slot giữ tạm và slot đã xác nhận.
- [ ] Tính capacity từ reservation chưa hết hạn và booking đã xác nhận; kiểm tra trùng lịch leader trong cả hai loại.
- [ ] Tạo thanh toán sau khi có reservation hợp lệ; không cho tạo link mới nếu reservation hết hạn hoặc đã có thanh toán hợp lệ.
- [ ] Xác nhận booking khi webhook/đối soát xác nhận đủ tiền và reservation còn hợp lệ, cập nhật nguyên tử với trạng thái liên quan.
- [ ] Hết hạn/hủy thanh toán thì giải phóng slot; worker chạy lại không giải phóng booking đã xác nhận.
- [ ] Xử lý thanh toán đến muộn: không giành lại slot đã cấp người khác; lưu khoản thu thực tế và chuyển luồng xử lý/hoàn tiền có audit.
- [ ] Giữ kiểm tra chữ ký webhook, số tiền, order, currency, link ID, giao dịch trùng và refund hiện có.
- [ ] Rà lại timeout 24 giờ chưa được phân công: tính từ thời điểm booking đủ điều kiện phân công, có chính sách riêng cho khoản thu muộn đang chờ xử lý.
- [ ] Không giả định refund tự động đã chuyển tiền: adapter mặc định hiện là ManualRefundProvider; khoản cần hoàn phải được theo dõi đến khi xử lý thực tế.

**Nghiệm thu:** hai request tranh slot cuối chỉ một request giữ được; webhook lặp không xác nhận hai lần; webhook muộn không làm vượt capacity; reservation hết hạn không cho phân công.

### Giai đoạn 5 — Phân công và report

- [ ] Trước khi assign: kiểm tra lại booking xác nhận, khoản thanh toán hợp lệ không bị refund đang chờ/đã hoàn, cấp 1 còn hiệu lực và không có case/assignment xung đột.
- [ ] Giữ nguyên quy tắc verificator Active, không thuộc dự án và không trùng lịch.
- [ ] Giao case là nhận bắt buộc; bổ sung thông báo trong hệ thống cho người được giao và xử lý chống thông báo lặp nếu cần.
- [ ] Cho verificator/Admin xem snapshot task evidence đã kiểm duyệt trong chi tiết case; không chỉ thấy JSON evidence riêng cũ.
- [ ] Theo chính sách mục 3, mở quyền đọc report hiện hành cho thành viên Active; không mở quyền sửa/nộp report hoặc xem case của nhóm khác.
- [ ] Giữ yêu cầu meeting đã kết thúc trước khi nộp report, chỉ người được giao nộp, nội dung bắt buộc, outcome và thao tác hoàn tất nguyên tử.
- [ ] Giữ phiên bản/audit, reminder/escalation và xử lý quá hạn hiện có; kiểm tra tương thích với các trạng thái mới.

**Nghiệm thu:** không phân công case chưa trả tiền/đã hoàn tiền hoặc cấp 1 hết hiệu lực; không hoàn tất cấp 2 thiếu report; quyền đọc đúng chính sách; không có bước accept/reject và không phát sinh yêu cầu xuất PDF.

### Giai đoạn 6 — Dữ liệu cũ, kiểm thử và tài liệu

- [ ] Định nghĩa cách xử lý dự án Completed/case Pending cũ chưa có kết quả cấp 1: cần kiểm tra thật, không tự backfill Passed.
- [ ] Giữ lịch sử case đã finalized/report cũ; không tự sửa kết quả xác thực trước đây khi migration.
- [ ] Với case cũ đã thanh toán/đã assign: lập phương án chuyển tiếp có kiểm soát, tránh khóa người dùng hoặc tự thu/hoàn tiền khi migrate.
- [ ] Cập nhật DTO/OpenAPI, hướng dẫn frontend, cấu hình worker, URL checker và reservation; ghi rõ API đề xuất nào được giữ/đổi tên khi triển khai.
- [ ] Sửa các test đang khẳng định phải thanh toán trước khi đặt lịch hoặc AI chỉ là advisory nếu hành vi đó đã được thay thế.
- [ ] Chạy test cho cấp 1, ProjectVerification, Assignment, Report, Transaction và worker; bổ sung test PostgreSQL cho transaction, capacity, unique constraint và chạy nhiều instance.
- [ ] Kiểm tra hồi quy moderation đầu vào, task report/revision, thanh toán, report version và quyền truy cập.
- [ ] Chạy tích hợp provider trong môi trường thử nghiệm; ghi rõ phần nào dùng fake và phần nào đã kiểm tra với provider thật.

## 5. Thứ tự ưu tiên và phụ thuộc

| Ưu tiên | Nhóm việc | Phụ thuộc |
| --- | --- | --- |
| P0 | Chốt chính sách nguồn evidence, phạm vi đọc file, sửa evidence, quyền xem report và giữ slot | Trước khi thay schema/API |
| P1 | Giai đoạn 1–2: dữ liệu cấp 1 và kiểm duyệt thật | Chính sách nguồn và provider |
| P1 | Giai đoạn 3: API kết quả và chặn cấp 2 | Kết quả cấp 1 có phiên bản |
| P1 | Giai đoạn 4: reservation và thứ tự thanh toán | Điều kiện vào cấp 2 |
| P2 | Giai đoạn 5: phân công, quyền đọc và snapshot trong case | Booking xác nhận và snapshot |
| Bắt buộc trước phát hành | Giai đoạn 6: dữ liệu cũ, kiểm thử, tài liệu | Thực hiện xuyên suốt, hoàn tất sau các thay đổi |

## 6. Tệp/thành phần dự kiến tác động

- `ProjectService`: hoàn tất dự án và tạo lượt kiểm duyệt.
- `ProjectTaskService`: quy tắc sửa nguồn, làm kết quả cũ hết hiệu lực hoặc khóa theo chu kỳ.
- Domain entities, DbContext, EF configurations/migrations: lượt cấp 1, snapshot, reservation và liên kết case.
- `VerificationAiTasks`, reviewer/contract kiểm duyệt evidence, `VerificationBackgroundWorker`: chạy kiểm tra, retry, phục hồi và hết hạn reservation.
- `ProjectVerificationService`, controller/DTO/mapping: điều kiện cấp 2, đọc cấp 1, đặt lịch và tương thích evidence cũ.
- `TransactionService`, `VerificationDeadlineTasks`: thanh toán sau giữ slot, webhook muộn, deadline và xử lý khoản thu cần hoàn.
- `AssignmentService`, mapping chi tiết case, notifications: kiểm tra trước phân công và cung cấp snapshot.
- `ReportService`: quyền đọc theo chính sách; giữ luồng nộp/chốt kết quả hiện có.
- Test và các tài liệu verification liên quan. Không thay nghiệp vụ của `ProjectModerationService`.

## 7. Checklist kết thúc

- [ ] Luồng moderation khi tạo dự án vẫn hoạt động.
- [ ] Completed tự khởi tạo kiểm duyệt chính TaskReport/TaskEvidence của sinh viên.
- [ ] Có kết quả cấp 1 độc lập, có lý do và phiên bản nguồn.
- [ ] Không có đường gọi API để bỏ qua điều kiện cấp 1.
- [ ] Cấp 2 tùy chọn; đặt lịch → thanh toán → phân công hoạt động đúng.
- [ ] Không vượt capacity, không xác nhận sai khi thanh toán đến muộn.
- [ ] Verificator được giao bắt buộc nhận; report vẫn là điều kiện hoàn tất.
- [ ] Quyền đọc report đúng chính sách đã chốt.
- [ ] Dữ liệu cũ và khoản thanh toán đang xử lý có phương án chuyển tiếp.
- [ ] Test và kiểm tra tích hợp có kết quả được ghi nhận trước phát hành.
- [ ] Không triển khai chức năng họp hoặc xuất PDF trong đợt này.
