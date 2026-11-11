# UniNet Dashboard — Hồ sơ assessor và phân công thẩm định

Ngày đối chiếu mã nguồn: 10/11/2026. Tài liệu bàn giao triển khai giao diện và tích hợp API, không thay đổi backend.

## 1. Đường dẫn API cần thống nhất

Yêu cầu đề cập `/api/admin/assessor-boardings` và `/api/admin/assessor-boardings/{id}`. Trong backend hiện tại, đường dẫn thực tế là **`/api/admin/assessor-onboardings`** và **`/api/admin/assessor-onboardings/{id}`**. Không tìm thấy route `assessor-boardings` hay alias tương ứng trong mã nguồn.

Dashboard phải gọi route thực tế bên dưới. Nếu môi trường triển khai có gateway đổi tên route, cần đối chiếu cấu hình môi trường trước khi tích hợp; không tự giả định route viết thiếu `on` hoạt động.

| Chức năng | Method và route thực tế |
|---|---|
| Danh sách hồ sơ assessor | GET `/api/admin/assessor-onboardings` |
| Chi tiết hồ sơ được chọn | GET `/api/admin/assessor-onboardings/{id}` |
| Assessor phù hợp chuyên ngành dự án | GET `/api/admin/projects/{projectId}/suitable-assessors` |
| Assessor có thể nhận lịch thẩm định | GET `/api/assignments/availability?projectVerificationId=...` |
| Tạo phân công | POST `/api/assignments` |

Các API trên cần phiên đăng nhập Admin và header `Authorization: Bearer <access-token>`. Các API chuyên ngành và phân công còn kiểm tra quyền `verification.evidence.review`; tài khoản phải đang hoạt động.

## 2. Màn hình danh sách hồ sơ assessor

### 2.1. Hành vi và bố cục

Thay luồng nhập ID để tra cứu bằng màn danh sách “Hồ sơ assessor”. Khi mở màn, tự gọi:

```http
GET /api/admin/assessor-onboardings?page=1&pageSize=20
```

Bố cục đề xuất:

```text
Hồ sơ assessor
[Tìm theo họ tên hoặc email...] [Trạng thái: Tất cả]

Họ và tên | Email | Trạng thái | Ngày tạo | Ngày nộp | Thao tác
Nguyễn A  | a@... | Chờ duyệt  | ...      | ...      | Xem chi tiết

Tổng số hồ sơ ...                          [Trước] [Trang ...] [Sau]
```

Không hiển thị ID hồ sơ, ID tài khoản hay ID người duyệt trong bảng, bộ lọc hoặc phần chi tiết. Không cung cấp ô “Tìm theo ID”. ID chỉ lưu nội bộ để chọn bản ghi và gọi API. Hai người trùng tên phải có thể phân biệt qua email.

### 2.2. Tham số và response danh sách

| Query | Cách dùng |
|---|---|
| `keyword` | Tìm chuỗi chứa trong họ tên hoặc email; tối đa 255 ký tự; bỏ qua nếu rỗng |
| `status` | Lọc theo AssessorOnboardingStatus; bỏ tham số khi chọn Tất cả |
| `page` | Bắt đầu từ 1 |
| `pageSize` | Từ 1 đến 100; mặc định 20 |

Backend sắp xếp ngày tạo mới nhất trước. Debounce tìm kiếm khoảng 300 ms, mã hóa query đúng cách; đổi từ khóa/trạng thái/kích thước trang phải về trang 1. Hủy hoặc bỏ qua response cũ khi người dùng đổi bộ lọc nhanh.

Ví dụ JSON minh họa cấu trúc (UUID bên dưới là dữ liệu nội bộ, không đưa lên giao diện):

```json
{
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1,
  "onboardings": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "emailNormalized": "assessor@example.com",
      "fullName": "Nguyễn Văn A",
      "status": "AwaitingApproval",
      "accountId": null,
      "autoApprove": false,
      "createdAt": "2026-11-01T08:00:00Z",
      "submittedAt": "2026-11-02T08:00:00Z",
      "signedAt": "2026-11-03T08:00:00Z",
      "autoApproveAt": null,
      "reviewedByAccountId": null,
      "reviewedAt": null,
      "reviewReason": null,
      "activatedAt": null
    }
  ]
}
```

Dùng `onboardings` làm nguồn bảng, không đọc nhầm thành `items`. Dùng metadata do backend trả để phân trang. Họ tên null hiển thị “Chưa cung cấp”; ngày null hiển thị “—”. Hiển thị thời gian theo múi giờ Dashboard, ví dụ Asia/Bangkok (UTC+7).

| Giá trị status (mã enum) | Nhãn đề xuất |
|---|---|
| Invited (0) | Đã gửi lời mời |
| ProfileSubmitted (1) | Đã nộp hồ sơ |
| AwaitingSignature (2) | Chờ ký hợp đồng |
| AwaitingApproval (3) | Chờ phê duyệt |
| AwaitingPassword (4) | Chờ đặt mật khẩu |
| Activated (5) | Đã kích hoạt |
| Rejected (6) | Đã từ chối |
| Revoked (7) | Đã thu hồi |
| Expired (8) | Đã hết hạn |
| ApprovalConflict (9) | Xung đột phê duyệt |

Response onboarding trả status dạng chuỗi; query có thể dùng tên enum tương ứng. Giá trị mới chưa có trong bảng ánh xạ cần có nhãn dự phòng, không làm lỗi màn hình.

### 2.3. Nút “Xem chi tiết”

Mỗi dòng có nút “Xem chi tiết”. Khi nhấn, lấy `row.id` đã lưu nội bộ, mở trang/panel/modal và gọi:

```http
GET /api/admin/assessor-onboardings/{row.id}
```

Luôn lấy chi tiết từ API, không chỉ phóng to dữ liệu dòng danh sách. Không yêu cầu Admin nhập, dán hay nhớ ID.

Nhóm thông tin hiển thị theo response chi tiết thực tế:

| Nhóm | Trường |
|---|---|
| Hồ sơ | `fullName`, `emailNormalized`, `status` |
| Ký hợp đồng | `signedName`, `signedAt`, `emailConfirmedAt`, `contractViewedAt`, `templateVersion` |
| Phê duyệt | `autoApprove`, `autoApproveAt`, `reviewedAt`, `reviewReason` |
| Kích hoạt | `activatedAt` |

Response còn có `id`, `accountId`, `reviewedByAccountId`, `contractSha256`; giữ nội bộ hoặc bỏ khỏi phần trình bày. Không tự suy ra tên người duyệt từ UUID. API chi tiết hiện không trả CCCD, mã số thuế, tài khoản ngân hàng, ngày tạo hoặc ngày nộp; không dựng thêm các trường này như dữ liệu từ API chi tiết. Nếu muốn hiển thị ngày tạo/nộp lấy từ dòng đã chọn, cần giữ riêng nguồn dữ liệu đó.

Có thể bổ sung nút xem hợp đồng PDF bằng GET `/api/admin/assessor-onboardings/{id}/contract`; hiển thị “Chưa có hợp đồng” khi API trả `CONTRACT_UNAVAILABLE`. Đây là tiện ích tùy chọn, không bắt buộc cho hai chức năng trong tài liệu.

### 2.4. Trạng thái giao diện

- Đang tải: skeleton bảng hoặc chỉ báo tải riêng cho panel chi tiết.
- Danh sách rỗng: “Chưa có hồ sơ assessor” hoặc “Không có hồ sơ phù hợp bộ lọc”.
- Lỗi mạng/server: hiển thị lỗi và nút Thử lại; giữ bộ lọc/trang đang xem.
- 400: xử lý query không hợp lệ (`INVALID_PAGINATION`, `INVALID_ONBOARDING_STATUS`, `INVALID_KEYWORD`).
- 401: xử lý hết phiên theo cơ chế đăng nhập hiện tại; 403: thông báo thiếu quyền.
- Chi tiết trả 404 `ONBOARDING_NOT_FOUND`: báo hồ sơ không còn tồn tại, cho quay lại và tải lại danh sách.
- Đóng chi tiết/quay lại giữ nguyên bộ lọc và trang; chuyển nhanh giữa các hồ sơ không để response cũ ghi đè hồ sơ mới.

## 3. Màn hình dự án chờ phân công thẩm định

### 3.1. Điểm vào và ngữ cảnh

Dùng danh sách hồ sơ thẩm định đang chờ phân công:

```http
GET /api/admin/project-verifications/pending-assignment?page=1&pageSize=10
```

Response có `page`, `pageSize`, `totalItems`, `totalPages`, `verifications`. Mỗi phần tử có `id` của hồ sơ thẩm định, `projectId`, `projectTitle`, `status`, lịch `scheduledStartAt`/`scheduledEndAt` và thông tin booking.

Bảng hiển thị tên dự án, trạng thái, lịch thẩm định và nút “Phân công assessor”. Không buộc Admin nhập projectId hay projectVerificationId. Nếu Dashboard đã có danh sách tương đương, tích hợp nút vào dòng dự án/hồ sơ đang được chọn.

Backend lọc hồ sơ Pending, chưa kết thúc, booking Confirmed, có `assignmentEligibleAt` và chưa có phân công chưa bị hủy. API phân công vẫn kiểm tra lại điều kiện tại thời điểm gửi.

### 3.2. Quy trình bắt buộc

1. Admin nhấn “Phân công assessor” ở một hồ sơ. Lưu nội bộ cả `verification.id` và `verification.projectId`; mở modal/panel với tên dự án và lịch thẩm định.
2. Gọi GET `/api/admin/projects/{projectId}/suitable-assessors?page=1&pageSize=20` để lấy assessor phù hợp chuyên ngành. API này chỉ đọc dữ liệu, không tạo phân công.
3. Sau khi lấy danh sách phù hợp, gọi GET `/api/assignments/availability?projectVerificationId={verification.id}` để kiểm tra khả năng nhận lịch của hồ sơ cụ thể.
4. Đối chiếu hai nguồn bằng `userId`. Chỉ cho chọn người vừa khớp chuyên ngành vừa xuất hiện trong availability. Có thể hiển thị người không rảnh ở trạng thái vô hiệu hóa, kèm nhãn “Không khả dụng cho lịch này”.
5. Admin chọn một người, xem lại họ tên assessor, tên dự án, lịch và nhấn “Xác nhận phân công”.
6. Chỉ lúc xác nhận mới gọi POST `/api/assignments` với `projectVerificationId` của hồ sơ và `assignedToUserId` của assessor đã chọn.
7. Thành công HTTP 201: thông báo “Phân công assessor thành công”, đóng panel và tải lại danh sách chờ phân công cùng danh sách phân công nếu đang hiển thị. Nếu trang cuối hết dữ liệu, chuyển về trang hợp lệ.

Không gọi POST khi mở panel, tải danh sách hay chỉ đổi lựa chọn. Khi đổi dự án/hồ sơ phải xóa lựa chọn cũ và tải lại cả hai nguồn.

### 3.3. Response assessor phù hợp và cách trình bày

```json
{
  "projectId": "22222222-2222-2222-2222-222222222222",
  "specialization": {
    "id": "33333333-3333-3333-3333-333333333333",
    "code": "SOFTWARE_ENGINEERING",
    "name": "Kỹ thuật phần mềm",
    "isActive": true
  },
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1,
  "assessors": [
    {
      "userId": "44444444-4444-4444-4444-444444444444",
      "accountId": "55555555-5555-5555-5555-555555555555",
      "fullName": "Nguyễn Văn A",
      "avatarUrl": null,
      "jobTitle": "Senior Engineer",
      "yearsOfExperience": 8,
      "specialization": {
        "id": "33333333-3333-3333-3333-333333333333",
        "code": "SOFTWARE_ENGINEERING",
        "name": "Kỹ thuật phần mềm",
        "isActive": true
      }
    }
  ]
}
```

Hiển thị avatar dự phòng, họ tên, chức danh, số năm kinh nghiệm, tên chuyên ngành, trạng thái khả dụng và lựa chọn radio. Không hiển thị `userId`, `accountId` hay ID chuyên ngành. `jobTitle=null` hiển thị “Chưa cập nhật”. Không thêm email, điểm phù hợp hoặc xếp hạng AI vì API không trả các trường đó.

API trả người có tài khoản Assessor Active, quyền thẩm định hiệu lực, được gán đúng chuyên ngành; loại người tạo dự án và thành viên active còn trong nhóm. Danh sách sắp theo tên rồi userId, không phải thứ tự điểm phù hợp.

Availability trả mảng trực tiếp:

```json
[
  {
    "userId": "44444444-4444-4444-4444-444444444444",
    "fullName": "Nguyễn Văn A"
  }
]
```

Phân trang theo metadata của suitable-assessors. Tính giao với availability trên từng trang; một trang không có người rảnh không đồng nghĩa tất cả các trang đều không có. Giữ điều hướng sang trang khác và phân biệt số người khớp chuyên ngành với số khả dụng trên trang hiện tại. Không dùng tổng `totalItems` làm tổng số người rảnh.

### 3.4. Request tạo phân công và phân biệt ID

```http
POST /api/assignments
Content-Type: application/json
Authorization: Bearer <access-token>
```

```json
{
  "projectVerificationId": "66666666-6666-6666-6666-666666666666",
  "assignedToUserId": "44444444-4444-4444-4444-444444444444"
}
```

| Giá trị | Nguồn | Dùng ở đâu |
|---|---|---|
| onboarding `id` | Danh sách hồ sơ assessor | GET chi tiết onboarding |
| `projectId` | Dòng hồ sơ thẩm định | GET suitable-assessors |
| `projectVerificationId` | `verifications[i].id` | GET availability và POST assignments |
| assessor `userId` | `assessors[i].userId` | `assignedToUserId` trong POST |
| assessor `accountId` | Response suitable-assessors | Không dùng làm assignedToUserId |

Các UUID mẫu chỉ minh họa; triển khai phải dùng dữ liệu API. Không dùng ID onboarding làm ID người nhận phân công. Onboarding và danh sách assessor nhận thẩm định là hai nguồn có mục đích khác nhau; hồ sơ chờ kích hoạt không tự trở thành người có thể được phân công.

Backend trả đối tượng AssignmentResponse khi tạo thành công, gồm `id`, `projectVerificationId`, `assignedToUserId`, `adminId`, `status`, các mốc phân công/deadline/lịch và các trường lifecycle. UI dùng response thực tế để cập nhật; không tự giả lập thành công trước HTTP 201.

### 3.5. Điều kiện, lỗi và chống gửi lặp

Khớp chuyên ngành không bảo đảm có thể nhận lịch. Backend tạo phân công còn kiểm tra hồ sơ Pending, chưa có báo cáo/kết thúc, booking được xác nhận bởi thanh toán hợp lệ, lịch tương lai hợp lệ, điều kiện nghiệp vụ và khả năng nhận lịch. Backend hiện chưa dùng chuyên ngành như điều kiện bắt buộc của POST assignments; Dashboard phải thực hiện bước giao hai danh sách theo quy trình trên.

| Trường hợp | Xử lý giao diện |
|---|---|
| `assessors=[]` | “Chưa có assessor phù hợp chuyên ngành”; khóa xác nhận |
| Có người khớp ngành nhưng không rảnh | Báo rõ theo trang; cho chuyển trang/tải lại, khóa chọn người không khả dụng |
| 409 `SPECIALIZATION_REQUIRED` | Yêu cầu bổ sung chuyên ngành dự án trước khi tiếp tục |
| 409 `SPECIALIZATION_INACTIVE` | Báo chuyên ngành đang ngừng sử dụng; không tự đổi ngành hoặc bỏ lọc |
| 404 `PROJECT_NOT_FOUND`/`VERIFICATION_NOT_FOUND` | Báo dữ liệu không còn tồn tại, tải lại danh sách |
| 409 `VERIFICATOR_UNAVAILABLE` | Xóa lựa chọn không còn hợp lệ, tải lại ứng viên/availability |
| 409 `ASSIGNMENT_EXISTS`/`ASSIGNMENT_CONFLICT` | Báo dữ liệu đã thay đổi, tải lại hồ sơ và danh sách phân công |
| `BOOKING_NOT_CONFIRMED`, `PAYMENT_REQUIRED`, `INVALID_VERIFICATION_SCHEDULE`, `VERIFICATION_LOCKED`, `REPORT_EXISTS` | Hiển thị nguyên nhân nghiệp vụ; dừng xác nhận, tải lại ngữ cảnh |
| 400 `INVALID_PAGINATION` | Sửa tham số phân trang; không hiển thị như danh sách rỗng |
| 401/403 | Xử lý phiên đăng nhập/thiếu quyền; không coi là không có ứng viên |
| Lỗi mạng/5xx | Thông báo lỗi, cho thử tải lại |

Khóa nút xác nhận trong lúc đang tải hai nguồn, chưa chọn người hợp lệ hoặc đang gửi POST. Sau timeout POST, tải lại trạng thái phân công để biết lần gửi trước đã thành công chưa; không tự gửi lại mù vì endpoint không cung cấp khóa idempotency trong contract hiện tại.

## 4. Tiêu chí nghiệm thu

- Mở màn hồ sơ assessor tự tải danh sách, có phân trang, lọc trạng thái và tìm theo tên/email; không còn ô tìm ID.
- Bảng và chi tiết không hiển thị UUID. Bấm Xem chi tiết gọi đúng GET với ID của dòng, kể cả khi hai hồ sơ trùng tên.
- Panel chi tiết xử lý tải, lỗi, null và quay lại đúng trang/bộ lọc.
- Chọn hồ sơ chờ phân công gọi suitable-assessors theo projectId, rồi kiểm tra availability theo projectVerificationId.
- Chỉ người thuộc cả hai nguồn mới có thể được xác nhận. Các trang sau vẫn truy cập được nếu trang đầu không có người rảnh.
- POST gửi đúng hai trường; assignedToUserId lấy từ userId, không lấy accountId hoặc onboardingId.
- Chỉ xác nhận mới tạo phân công; nút không gửi lặp; thành công cập nhật danh sách.
- Kiểm tra danh sách rỗng, ngành thiếu/inactive, hết phiên/thiếu quyền, xung đột lịch và phân công đồng thời; không che lỗi thành dữ liệu rỗng.

## 5. Nguồn mã đã đối chiếu

Đường dẫn bên dưới tính từ repository UniNet_BE:

- `UniNet.API/Controllers/AssessorOnboardingController.cs`
- `UniNet.Application/Services/AssessorOnboardingService.cs`
- `UniNet.Application/Services/AssessorActivationService.cs`
- `UniNet.Domain/Entities/AssessorOnboarding.cs`
- `UniNet.API/Controllers/SpecializationsController.cs`
- `UniNet.Application/Services/SpecializationService.cs`
- `UniNet.Application/DTOs/Projects/SpecializationContracts.cs`
- `UniNet.API/Controllers/AdminProjectVerificationController.cs`
- `UniNet.Application/Services/AdminProjectVerificationService.cs`
- `UniNet.Application/DTOs/Projects/ProjectVerificationContract.cs`
- `UniNet.API/Controllers/AssignmentController.cs`
- `UniNet.Application/Services/AssignmentService.cs`
- `UniNet.Application/DTOs/Projects/AssignmentContract.cs`

Tài liệu đối chiếu contract và quy trình trong mã nguồn; chưa kiểm thử trên Dashboard hoặc môi trường API đang triển khai.
