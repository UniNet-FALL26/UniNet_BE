# Hợp đồng và OTP onboarding Assessor

## Luồng đã triển khai

Worker lấy hồ sơ ProfileSubmitted, giải mã snapshot, sinh PDF và lưu file riêng tư theo OnboardingId cùng SHA-256 đầy đủ. Trạng thái AwaitingSignature và email hợp đồng được ghi trong cùng giao dịch database. PDF lỗi được thử lại sau 5 phút và có GenerationErrorCode để kiểm tra.

Assessor mở link trong email, yêu cầu OTP đọc, xác thực email, mở PDF, yêu cầu OTP ký và xác nhận tên cùng câu cam kết. OTP đọc và ký có mục đích riêng. OTP ký gắn với hồ sơ và hash PDF. Sau khi ký, hồ sơ chuyển AwaitingApproval. Hồ sơ Auto được đặt AutoApproveAt sau 15 phút. Worker duyệt và cấp tài khoản là bước triển khai tiếp theo.

Email được lưu mã hóa trong outbox. Worker nhận việc bằng lease, kiểm tra link hoặc OTP còn hiệu lực trước khi gửi, thử lại tối đa 10 lần và xóa payload bí mật sau khi gửi hoặc hủy. SMTP dùng STARTTLS trên cổng cấu hình, mặc định 587. SMTP có thể gửi trùng khi tiến trình dừng sau lúc nhà cung cấp nhận email nhưng trước khi ghi SentAt. Message-ID ổn định giúp đối chiếu, còn token và OTP vẫn chỉ dùng theo trạng thái hiện tại.

## Cấu hình

Chức năng mặc định tắt. Cấu hình bằng environment variables hoặc mục AssessorOnboarding trong appsettings. Không đưa mật khẩu SMTP hoặc khóa vào git.

Ví dụ PowerShell chạy trên máy phát triển, thay các đường dẫn và địa chỉ bằng giá trị thực tế.

~~~powershell
$env:AssessorOnboarding__Enabled = 'true'
$env:AssessorOnboarding__PublicBaseUrl = 'https://localhost:7042'
$env:AssessorOnboarding__StoragePath = 'D:UniNetDataassessor-contracts'
$env:AssessorOnboarding__KeyRingPath = 'D:UniNetDatakeyring'
$env:AssessorOnboarding__TemplatePath = 'D:UniNetData	emplatesassessor-v1.txt'
$env:AssessorOnboarding__TemplateVersion = 'TEMPLATE_ASSESSOR_AGREEMENT_V1.0'
$env:AssessorOnboarding__FontPath = 'C:WindowsFontsarial.ttf'
$env:AssessorOnboarding__SignatureStatement = 'Nội dung xác nhận đã được duyệt cho phiên bản hợp đồng này.'
$env:AssessorOnboarding__Smtp__Host = 'smtp.example.com'
$env:AssessorOnboarding__Smtp__Port = '587'
$env:AssessorOnboarding__Smtp__From = 'noreply@example.com'
$env:AssessorOnboarding__Smtp__Username = 'smtp-user'
$env:AssessorOnboarding__Smtp__Password = 'THAY_BANG_MAT_KHAU_THUC_TE'
dotnet run --project .UniNet.API
~~~

PublicBaseUrl là origin của backend, gồm giao thức và host với port nếu có. HTTPS bắt buộc ngoài Development. Khi chạy nhiều instance, dùng chung khóa Data Protection và vùng lưu PDF. StoragePath phải ở ngoài wwwroot, có quyền đọc ghi cho tài khoản chạy ứng dụng. FontPath trỏ đến font TrueType có đủ ký tự tiếng Việt và quyền sử dụng phù hợp với môi trường triển khai.

Mẫu hợp đồng là tệp UTF-8 dạng văn bản thuần, xuống dòng và tự phân trang. Các placeholder được hỗ trợ gồm {{OnboardingId}}, {{FullName}}, {{Email}}, {{CitizenId}}, {{TaxCode}}, {{BankName}}, {{BankAccountNumber}}, {{BankAccountHolder}}, {{TemplateVersion}} và {{SignatureStatement}}. Mỗi phiên bản mẫu cần có nội dung đã được duyệt. Khi thay nội dung, tăng TemplateVersion. PDF đã phát hành và câu xác nhận trên hồ sơ được giữ nguyên khi cấp lại link.

Để kiểm tra trên Development bằng file .eml, đặt AssessorOnboarding__Smtp__From thành email thử nghiệm và AssessorOnboarding__Smtp__PickupDirectory thành đường dẫn tuyệt đối ngoài wwwroot, ví dụ D:UniNetDatamail-pickup. Chế độ này ghi email vào thư mục để mở bằng trình đọc email. Chỉ định PickupDirectory sẽ ưu tiên ghi file thay cho kết nối SMTP. Giới hạn quyền truy cập vì file chứa OTP và link. Production từ chối chế độ pickup.

Áp dụng migration AddAssessorContractSigning trước khi bật worker. Kiểm tra cấu hình bằng môi trường thử nghiệm trước khi nhận hồ sơ thật. Dùng kho khóa từ giai đoạn nộp form để giải mã được dữ liệu đã thu thập.

~~~powershell
dotnet ef database update AddAssessorContractSigning --project UniNet.Infrastructure --startup-project UniNet.API
~~~

## API

Mọi token được truyền trong JSON body. Link trang web dùng fragment #token để tránh ghi token vào URL request. Các trang giữ token phiên trong bộ nhớ trình duyệt. Làm mới trang hợp đồng cần mở lại link email và xác thực lại.

| Method | Đường dẫn | Body và kết quả |
|---|---|---|
| POST | /api/assessor-onboarding/contract/read-otp | { "token": "<contract-token>" } → 202 |
| POST | /api/assessor-onboarding/contract/verify-read-otp | { "token": "<contract-token>", "code": "123456" } → { "token": "<session-token>" } |
| POST | /api/assessor-onboarding/contract/inspect | { "token": "<session-token>" } → tên, phiên bản, hash PDF, câu xác nhận và hạn phiên |
| POST | /api/assessor-onboarding/contract/pdf | { "token": "<session-token>" } → PDF riêng tư |
| POST | /api/assessor-onboarding/contract/sign-otp | { "token": "<session-token>" } → 202, yêu cầu PDF đã được mở |
| POST | /api/assessor-onboarding/contract/sign | { "token": "<session-token>", "code": "123456", "signedName": "Nguyễn Văn A", "contractSha256": "<hash-from-inspect>", "accepted": true } → AwaitingApproval |
| POST | /api/admin/assessor-onboardings/{id}/resend-contract | Admin, cấp link mới cho cùng PDF đang AwaitingSignature |
| POST | /api/admin/assessor-onboardings/invitations/{id}/revoke | Admin, thu hồi từ Invited đến AwaitingApproval |

Link hợp đồng có hạn 3 ngày. Phiên đọc có hạn 30 phút và luôn bị giới hạn bởi hạn link. OTP có hạn 10 phút, tối đa 5 lần nhập sai, cách lần yêu cầu trước ít nhất 60 giây, tổng tối đa 3 lần mỗi giờ cho cả OTP đọc và ký. Cấp lại link có cooldown 60 giây, vô hiệu hóa link, phiên đọc và OTP cũ. Rate limiter áp dụng 30 request/phút/IP trên nhóm API hợp đồng. Khi dùng reverse proxy, cấu hình trusted forwarded headers để server xác định IP người gọi đúng.

## Thử luồng

1. Áp dụng migration, tạo thư mục lưu trữ và mẫu UTF-8 thực tế, đặt cấu hình rồi chạy API.
2. Tạo lời mời như giai đoạn trước và nộp form.
3. Chờ worker khoảng 5–10 giây, kiểm tra email hoặc pickup. Mở link hợp đồng.
4. Yêu cầu OTP đọc, lấy mã mới trong email và xác thực. Kiểm tra PDF.
5. Sau ít nhất 60 giây kể từ lần gửi OTP trước, yêu cầu OTP ký. Nhập mã mới, tên và tích câu cam kết rồi ký.
6. Kiểm tra hồ sơ AwaitingApproval, SignedName, SignedAt, ContractViewedAt, EmailConfirmedAt, TemplateVersion, ContractSha256 và SignatureStatement. Kiểm tra AccountId vẫn null ở giai đoạn này.

Chạy kiểm thử bằng dotnet test UniNet.Tests --filter FullyQualifiedName~AssessorContract.
