# Onboarding và ký hợp đồng online cho Assessor

Admin gửi lời mời qua email. Assessor mở link, nhập họ tên, CCCD và tài khoản ngân hàng rồi xác nhận. API tạo và lưu hợp đồng cùng phiên bản và SHA-256, sau đó trả đường dẫn để mở ngay trang ký. Assessor đọc toàn bộ hợp đồng và phụ lục, kiểm tra họ tên, tích đồng ý và xác nhận ký. Hồ sơ chuyển sang AwaitingApproval. Admin duyệt và hệ thống gửi liên kết đặt mật khẩu.

Trang hồ sơ và ký nằm trên origin backend. Link ký dùng token của lời mời đã gửi qua email, có hiệu lực 3 ngày sau khi nộp hồ sơ. Mở lại email mời để tiếp tục nếu đóng trang. Admin vẫn có thể gửi lại link khi AwaitingSignature. Token cũ bị vô hiệu hóa khi cấp link mới.

Mục tải PDF trả bản hợp đồng và phụ lục đã hiển thị. Sau khi ký, bản tải thêm trang xác nhận chứa người ký, thời điểm, phiên bản và hash của nội dung đã xác nhận.

## Cấu hình triển khai

- Bật AssessorOnboarding__Enabled và đặt AssessorOnboarding__PublicBaseUrl thành origin HTTPS của backend.
- Giữ cấu hình mail đang dùng. Giữ AssessorOnboarding__StoragePath và AssessorOnboarding__KeyRingPath trên nơi lưu bền vững, ngoài wwwroot. AssessorOnboarding__FontPath trỏ tới font TrueType hỗ trợ tiếng Việt trên server.
- Để trống AssessorOnboarding__TemplatePath để dùng mẫu ASSESSOR-AGREEMENT-V2 đi kèm ứng dụng. Thiết lập các biến với tiền tố AssessorOnboarding__ gồm CompanyName, CompanyTaxCode, CompanyAddress, RepresentativeName, RepresentativePosition, CompanyEmail, CompanyPhone, MinimumWithdrawalAmount và PaymentProcessingDays theo dữ liệu thực tế.
- Nếu triển khai một bản mẫu riêng, cấu hình TemplatePath và TemplateVersion tương ứng. PDF đã phát hành tiếp tục dùng đúng bản đã lưu. Thay đổi này không yêu cầu migration mới.

## API công khai

Tất cả là POST, gửi token trong JSON body. Link trên email dùng fragment để token không đi trong URL request.

| Đường dẫn dưới /api/assessor-onboarding | Body và phản hồi |
|---|---|
| /profile/inspect | token → email và contractLink nếu đã nộp |
| /profile | token, fullName, citizenId, bankName, bankAccountNumber, bankAccountHolder → id, status AwaitingSignature, contractLink |
| /contract/inspect | token → fullName, pages, templateVersion, signatureStatement, contractSha256, signedAt, signedName, contractTokenExpiresAt |
| /contract/pdf | token → PDF |
| /contract/sign | token, signedName, contractSha256, accepted → status AwaitingApproval, signedName, signedAt |

Các API ký không dùng OTP. Họ tên ký phải khớp với hồ sơ và hash phải khớp bản hợp đồng đã mở. Lời mời mới yêu cầu Admin duyệt, kể cả khi Dashboard cũ còn gửi autoApprove.

## Duyệt hồ sơ và kích hoạt tài khoản

Phản hồi `approve` (kể cả HTTP 409 ApprovalConflict) và `reject` trả thêm `reviewedByAccountId`, `reviewedAt`, `reviewReason`, khớp dữ liệu GET chi tiết. ID admin lấy từ JWT người thực hiện, có thể khác người tạo lời mời. Gọi lại thao tác đã hoàn tất giữ nguyên thông tin review ban đầu. Duyệt tự động giữ `reviewedByAccountId` là null.

Admin sử dụng JWT của tài khoản Admin đang Active. API xem hồ sơ trả về thông tin xét duyệt và bằng chứng ký. API PDF kiểm tra quyền Admin và tính toàn vẹn của file trước khi trả về.

| Method | Đường dẫn | Input và kết quả |
|---|---|---|
| GET | /api/admin/assessor-onboardings/{id} | Xem trạng thái và bằng chứng xét duyệt |
| GET | /api/admin/assessor-onboardings/{id}/contract | Xem PDF riêng tư để kiểm tra hồ sơ |
| POST | /api/admin/assessor-onboardings/{id}/approve | Body trống, trả id, status và accountId |
| POST | /api/admin/assessor-onboardings/{id}/reject | { "reason": "Lý do gửi cho assessor" }, tối đa 2000 ký tự |
| POST | /api/admin/assessor-onboardings/{id}/resend-password | Body trống, cấp lại email đặt mật khẩu khi AwaitingPassword |
| POST | /api/assessor-onboarding/password/inspect | { "token": "<activation-token>" }, kiểm tra link và thông tin tài khoản |
| POST | /api/assessor-onboarding/password | { "token": "<activation-token>", "password": "Password123", "confirmPassword": "Password123" }, trả Activated |
| POST | /api/assessor-onboarding/password/resend | { "token": "<activation-token>" }, có thể dùng token đã hết hạn nếu vẫn là token hiện tại |

Khi duyệt thành công, account có role Assessor, trạng thái Pending, EmailVerified true và PasswordHash null. Profile Users chứa họ tên và nickname. Hồ sơ chuyển AwaitingPassword. Tài khoản, liên kết hồ sơ và email đặt mật khẩu được ghi trong cùng giao dịch. Gọi lại API approve sau khi đã duyệt trả kết quả hiện có, giữ nguyên account và link đã phát hành.

Nếu email trùng tài khoản khác, hồ sơ chuyển ApprovalConflict, ReviewReason là EMAIL_ALREADY_REGISTERED và dừng tự động duyệt. API approve trả HTTP 409 cùng status ApprovalConflict. Admin liên hệ assessor để xử lý, có thể từ chối hồ sơ cũ rồi tạo lời mời bằng email phù hợp. Mỗi thao tác cấp tài khoản dùng revision và giao dịch để tránh tạo trùng khi Admin và worker chạy đồng thời.

Email duyệt chứa trang /assessor-onboarding/activate.html#token=... có hạn 48 giờ. Token chỉ được dùng một lần và chỉ có bản hash trong hồ sơ. POST inspect chỉ kiểm tra link, POST đặt mật khẩu mới tiêu thụ token và chuyển account Active cùng hồ sơ Activated trong một giao dịch. Mật khẩu dùng cùng quy tắc với auth hiện tại, từ 8 đến 1024 ký tự, có chữ và số, khớp xác nhận, được lưu bằng PasswordHasher. Sau khi kích hoạt, assessor đăng nhập qua API auth hiện có.

Gửi lại link có cooldown 60 giây, tối đa 3 email SetPassword mỗi giờ, tính cả lần gửi khi duyệt. Cấp lại link sẽ vô hiệu hóa link cũ. Endpoint resend công khai trả 202 chung cho token không còn đủ điều kiện, tránh cung cấp thông tin trạng thái tài khoản. Worker bỏ qua email đặt mật khẩu đã bị thay thế, hết hạn hoặc khi account đã kích hoạt hay bị thay đổi trạng thái. Lý do từ chối được gửi qua email, nên chỉ nhập nội dung được phép gửi cho assessor.

### Thử từ hồ sơ đã ký

1. Lấy id hồ sơ và gọi GET /api/admin/assessor-onboardings/{id}. Kiểm tra status AwaitingApproval, SignedAt và EmailConfirmedAt.
2. Gọi POST /api/admin/assessor-onboardings/{id}/approve bằng Admin, body trống. Kết quả là AwaitingPassword và accountId.
3. Kiểm tra email thật hoặc pickup theo cấu hình hiện có. Mở link kích hoạt trong email.
4. Đặt mật khẩu và nhập lại mật khẩu. Trang báo tài khoản đã kích hoạt. Nếu dùng Postman, lấy token sau #token= và gửi POST /api/assessor-onboarding/password.
5. Đăng nhập bằng email assessor và mật khẩu mới qua POST /api/auth/login. Kiểm tra account role Assessor và trạng thái Active.
6. Gọi lại link đặt mật khẩu đã dùng sẽ trả HTTP 410. Hồ sơ giữ nguyên Activated.

Migration AddAssessorPasswordTokenIndex bổ sung unique index cho SetPasswordTokenHash. Áp dụng migration trước khi triển khai phiên bản mới.

~~~powershell
dotnet ef database update AddAssessorPasswordTokenIndex --project UniNet.Infrastructure --startup-project UniNet.API
~~~

## Danh sách onboarding dành cho Admin

GET `/api/admin/assessor-onboardings?status=AwaitingApproval&keyword=nguyen&page=1&pageSize=20`

- JWT role Admin và account Admin đang Active. Không giới hạn theo người tạo lời mời: xem được tất cả hồ sơ trong hệ thống.
- Tất cả query đều tùy chọn. Không truyền status để lấy mọi trạng thái. status nhận tên enum hoặc số 0–9 theo AssessorOnboardingStatus; status trong response là chuỗi tên.
- keyword tìm không phân biệt hoa thường theo emailNormalized hoặc fullName; trim khoảng trắng, tối đa 255 ký tự. Từ khóa trống không lọc; không tìm trên CCCD/thuế/ngân hàng.
- page mặc định 1, phải >=1; pageSize mặc định 20, từ 1 đến 100. Sắp xếp createdAt giảm dần, rồi id tăng dần để phân trang ổn định.
- Body 200: `{ page, pageSize, totalItems, totalPages, onboardings: [...] }`. Trang vượt phạm vi trả mảng rỗng; không có kết quả thì totalPages=0.
- Mỗi item gồm id, emailNormalized, fullName, status, accountId, autoApprove, createdAt, submittedAt, signedAt, autoApproveAt, reviewedByAccountId, reviewedAt, reviewReason, activatedAt. Các trường chưa có giá trị là null.
- Không trả entity thô, token/link mời, token hash, OTP, session, dữ liệu mã hóa, CCCD hoặc tài khoản ngân hàng. Dùng GET `/{id}` và `/{id}/contract` hiện có khi mở chi tiết.
- Đọc trạng thái đang lưu trong database; GET không tự đổi trạng thái hồ sơ hoặc gia hạn lời mời.
- Lỗi 400: INVALID_PAGINATION, INVALID_ONBOARDING_STATUS, INVALID_KEYWORD. Query không bind được enum/int có thể trả ValidationProblemDetails. 401 nếu thiếu JWT; 403 nếu không phải Admin Active.
- Dashboard có thể dùng API này để làm danh sách/hàng đợi onboarding thay cho giải pháp nhập ID trước đây. Bỏ query status khi chọn “Tất cả”, đặt page=1 khi đổi bộ lọc.
- Không cần migration database cho endpoint này.

## Khi Dashboard không mở được hợp đồng

Dashboard gọi API của môi trường đã cấu hình. Khi localhost và Render dùng chung database, Render vẫn cần đọc được tệp PDF gốc tương ứng tại StoragePath. Nếu tệp chỉ tồn tại trên máy local, hãy kiểm thử với API local hoặc phát hành hồ sơ thử mới từ Render. Với hồ sơ đã ký cần chuyển sang Render, chuyển đúng PDF gốc qua kênh riêng tư và giữ nguyên thư mục tương đối dưới StoragePath để kiểm tra SHA-256 vẫn khớp.

Nếu trang ký hiển thị TEST_V1, hồ sơ đang dùng bản mẫu thử đã phát hành. Bỏ TemplatePath cũ và nộp một hồ sơ mới để kiểm tra mẫu đi kèm, sau khi cấu hình thông tin Bên A và thanh toán.
