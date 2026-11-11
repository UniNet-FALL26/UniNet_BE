# Hợp đồng và OTP onboarding Assessor

## Luồng đã triển khai

Worker lấy hồ sơ ProfileSubmitted, giải mã snapshot, sinh PDF và lưu file riêng tư theo OnboardingId cùng SHA-256 đầy đủ. Trạng thái AwaitingSignature và email hợp đồng được ghi trong cùng giao dịch database. PDF lỗi được thử lại sau 5 phút và có GenerationErrorCode để kiểm tra.

Assessor mở link trong email, yêu cầu OTP đọc, xác thực email, mở PDF, yêu cầu OTP ký và xác nhận tên cùng câu cam kết. OTP đọc và ký có mục đích riêng. OTP ký gắn với hồ sơ và hash PDF. Sau khi ký, hồ sơ chuyển AwaitingApproval. Hồ sơ Auto được đặt AutoApproveAt sau 15 phút. Worker duyệt và cấp tài khoản Pending sau khi đến hạn. Assessor đặt mật khẩu để kích hoạt.

Email được lưu mã hóa trong outbox. Worker nhận việc bằng lease, kiểm tra link hoặc OTP còn hiệu lực trước khi gửi, thử lại tối đa 10 lần và xóa payload bí mật sau khi gửi hoặc hủy. SMTP dùng STARTTLS trên cổng cấu hình, mặc định 587. SMTP có thể gửi trùng khi tiến trình dừng sau lúc nhà cung cấp nhận email nhưng trước khi ghi SentAt. Message-ID ổn định giúp đối chiếu, còn token và OTP vẫn chỉ dùng theo trạng thái hiện tại.

## Cấu hình

Chức năng mặc định tắt. Cấu hình bằng environment variables hoặc mục AssessorOnboarding trong appsettings. Không đưa mật khẩu SMTP hoặc khóa vào git.

Ví dụ PowerShell chạy trên máy phát triển, thay các đường dẫn và địa chỉ bằng giá trị thực tế.

~~~powershell
$env:AssessorOnboarding__Enabled = 'true'
$env:AssessorOnboarding__PublicBaseUrl = 'https://localhost:7042'
$env:AssessorOnboarding__StoragePath = 'D:/UniNetData/assessor-contracts'
$env:AssessorOnboarding__KeyRingPath = 'D:/UniNetData/keyring'
$env:AssessorOnboarding__TemplatePath = 'D:/UniNetData/templates/assessor-v1.txt'
$env:AssessorOnboarding__TemplateVersion = 'TEMPLATE_ASSESSOR_AGREEMENT_V1.0'
$env:AssessorOnboarding__FontPath = 'C:/Windows/Fonts/arial.ttf'
$env:AssessorOnboarding__SignatureStatement = 'Nội dung xác nhận đã được duyệt cho phiên bản hợp đồng này.'
$env:AssessorOnboarding__Smtp__Host = 'smtp.example.com'
$env:AssessorOnboarding__Smtp__Port = '587'
$env:AssessorOnboarding__Smtp__From = 'noreply@example.com'
$env:AssessorOnboarding__Smtp__Username = 'smtp-user'
$env:AssessorOnboarding__Smtp__Password = 'THAY_BANG_MAT_KHAU_THUC_TE'
dotnet run --project ./UniNet.API --launch-profile https
~~~

PublicBaseUrl là origin của backend, gồm giao thức và host với port nếu có. HTTPS bắt buộc ngoài Development. Khi chạy nhiều instance, dùng chung khóa Data Protection và vùng lưu PDF. StoragePath phải ở ngoài wwwroot, có quyền đọc ghi cho tài khoản chạy ứng dụng. FontPath trỏ đến font TrueType có đủ ký tự tiếng Việt và quyền sử dụng phù hợp với môi trường triển khai.

### Link email trên môi trường deploy (Render)

Các trang profile.html, contract.html và activate.html đã nằm trong `UniNet.API/wwwroot/assessor-onboarding` và được backend phục vụ. Không cần đăng nhập UniNet_FE để mở các trang này. JavaScript gọi API cùng origin với trang đang mở.

Trên Render, đặt biến môi trường của dịch vụ backend:

```text
AssessorOnboarding__PublicBaseUrl=https://uninet-be.onrender.com
```

Không thêm `/api`, `/assessor-onboarding` hoặc token vào giá trị cấu hình. Lưu cấu hình và khởi động lại/redeploy dịch vụ. Biến môi trường triển khai ưu tiên hơn `.env`; giữ localhost trong `.env` cho phát triển cục bộ không ảnh hưởng khi Render có biến ghi đè.

Sau đó email hợp đồng mới sẽ chứa link dạng `https://uninet-be.onrender.com/assessor-onboarding/contract.html#token=...`; email đặt mật khẩu dùng cùng origin với đường dẫn activate.html. API tạo lời mời hiện trả profileLink dạng tương đối, nên Dashboard ghép với origin backend deploy khi chia sẻ link điền hồ sơ.

Dashboard phải gọi backend deploy để tạo hồ sơ/phát hành link. Không chỉ thay hostname của link được tạo bởi backend local: token, hồ sơ, khóa Data Protection và PDF phải thuộc môi trường phục vụ link. Các trang deploy cũng gọi API deploy, không gọi máy localhost của Admin.

Email đã gửi không tự đổi URL khi sửa cấu hình. Với hồ sơ còn AwaitingSignature, Admin dùng `POST /api/admin/assessor-onboardings/{id}/resend-contract` trên backend deploy để phát hành link mới, theo điều kiện/hạn mức gửi lại của API. Với hồ sơ AwaitingPassword, dùng `POST /api/admin/assessor-onboardings/{id}/resend-password`. Nếu hồ sơ chỉ tồn tại trong database local thì cần tạo lời mời mới trên deploy hoặc thực hiện một kế hoạch chuyển dữ liệu riêng; không giả định đổi tên miền sẽ chuyển hồ sơ.

Các đường dẫn StoragePath, TemplatePath, FontPath và KeyRingPath phải hợp lệ trên máy chủ deploy. Không sao chép đường dẫn ổ D hoặc font Windows từ máy phát triển sang môi trường Linux. Giữ PDF và key ring trên vùng lưu bền vững để không mất khả năng đọc hợp đồng sau khi redeploy.

Mẫu hợp đồng là tệp UTF-8 dạng văn bản thuần, xuống dòng và tự phân trang. Các placeholder được hỗ trợ gồm {{OnboardingId}}, {{FullName}}, {{Email}}, {{CitizenId}}, {{TaxCode}}, {{BankName}}, {{BankAccountNumber}}, {{BankAccountHolder}}, {{TemplateVersion}} và {{SignatureStatement}}. Mỗi phiên bản mẫu cần có nội dung đã được duyệt. Khi thay nội dung, tăng TemplateVersion. PDF đã phát hành và câu xác nhận trên hồ sơ được giữ nguyên khi cấp lại link.

Để kiểm tra trên Development bằng file .eml, đặt AssessorOnboarding__Smtp__From thành email thử nghiệm và AssessorOnboarding__Smtp__PickupDirectory thành đường dẫn tuyệt đối ngoài wwwroot, ví dụ D:/UniNetData/mail-pickup. Chế độ này ghi email vào thư mục để mở bằng trình đọc email. Chỉ định PickupDirectory sẽ ưu tiên ghi file thay cho kết nối SMTP. Giới hạn quyền truy cập vì file chứa OTP và link. Production từ chối chế độ pickup.

Áp dụng migration AddAssessorContractSigning trước khi bật worker. Kiểm tra cấu hình bằng môi trường thử nghiệm trước khi nhận hồ sơ thật. Dùng kho khóa từ giai đoạn nộp form để giải mã được dữ liệu đã thu thập.

Trong Development, nếu đã nộp hồ sơ bằng kho khóa mặc định của ASP.NET Core, giữ nguyên cấu hình đó và bỏ biến KeyRingPath khỏi ví dụ. Chế độ này cho phép bật hợp đồng bằng kho khóa mặc định đang dùng. Đổi thư mục khóa hoặc application name sau khi đã nộp hồ sơ có thể khiến dữ liệu cũ không giải mã được. Khi triển khai ngoài Development, KeyRingPath vẫn bắt buộc.

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
6. Kiểm tra hồ sơ AwaitingApproval, SignedName, SignedAt, ContractViewedAt, EmailConfirmedAt, TemplateVersion, ContractSha256 và SignatureStatement. Với chế độ duyệt tay, kiểm tra AccountId vẫn null trước khi Admin duyệt.

Chạy kiểm thử bằng dotnet test UniNet.Tests --filter FullyQualifiedName~AssessorContract.

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

Worker gọi cùng nghiệp vụ duyệt với API Admin. Hồ sơ phải AwaitingApproval, đã ký, có AutoApprove true và đến AutoApproveAt. Với luồng ký hiện tại, thời điểm này là 15 phút sau SignedAt. ReviewedByAccountId null biểu thị duyệt tự động. Admin có thể duyệt sớm hoặc từ chối trước khi worker xử lý.

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

Để thử duyệt tự động, tạo lời mời AutoApprove true, hoàn tất ký rồi chờ 15 phút và chu kỳ worker. Để thử gửi lại, chờ 60 giây sau lúc duyệt, dùng API resend-password của Admin hoặc API resend công khai với token hiện tại. Dùng link trong email mới nhất.

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
