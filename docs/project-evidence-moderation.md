# Kiểm duyệt evidence cấp 1 — Giai đoạn 1 và 2

Triển khai ngày 17/10/2026 theo `project-verification-gap-plan.md`. Tài liệu này mô tả phần đã thực hiện; không thay đổi nghiệp vụ cấp 2.

## Phạm vi đã được xác nhận

- Kiểm tra Content của TaskReport và URL trong report/TaskEvidence. Không tải, đọc, OCR hoặc xác nhận an toàn nội dung file.
- Gemini dùng lại provider `GeminiVerificationAiReviewer` và transport hiện có, với contract content riêng. Completeness check vẫn giữ contract và tác vụ cũ.
- Google Safe Browsing v4 tra URL trên các danh sách được cấu hình; không dùng suy đoán của Gemini thay cho kết quả tra cứu.
- Giữ hành vi sửa/xóa task/report hiện có: mỗi thay đổi nguồn tạo snapshot mới, giữ nguyên snapshot cũ. Chưa bổ sung khóa theo thanh toán/phân công.
- Lấy report có Version lớn nhất của từng SubmittedById trong từng task, bao gồm mọi trạng thái report và task Cancelled. Không tự đặt điều kiện tất cả task phải Completed.
- Giữ `ProjectModerationService` và luồng kiểm duyệt khi tạo dự án. Không thêm API cấp 1, không đổi điều kiện cấp 2, đặt lịch, thanh toán, phân công, họp hoặc quyền report.

## Dữ liệu và giao dịch

`ProjectEvidenceModerations` lưu phiên bản lượt kiểm duyệt, SHA-256 nguồn, snapshot bất biến, hash/snapshot chính sách, trạng thái, lease, lịch retry và kết quả tổng hợp. `EvidenceModerationAttempts` giữ từng lần xử lý và kết quả chi tiết.

Snapshot có ProjectId, ProjectTaskId/status, TaskReportId/Version/SubmittedById/Content/status/SubmittedAt và TaskEvidenceId/URL/metadata. Sắp xếp ổn định trước khi hash. Không đưa khóa API hoặc timestamp xử lý vào hash. Version tăng khi nguồn hoặc chính sách thay đổi; refresh lặp lại cùng nguồn/chính sách không tạo bản mới.

`PATCH /api/projects/status` chuyển Active → Completed và tạo lượt Pending trong cùng transaction Serializable. Không gọi provider trong transaction này và không cần package. Yêu cầu hoàn tất lặp lại vẫn trả 409 theo hành vi cũ, không tạo lượt trùng.

Các thao tác ghi của `ProjectTaskService` lưu dữ liệu nguồn và refresh snapshot trong cùng transaction. Revision vẫn cần trạng thái RevisionRequested theo quy tắc cũ. Xóa task không xóa lịch sử snapshot: lượt cũ có SupersededAt, lượt mới ghi lại tập dữ liệu còn tồn tại. FK snapshot không trỏ trực tiếp đến task/report/evidence nên việc xóa nguồn không cascade vào lịch sử.

Unique index bảo đảm một lượt hiện hành trên mỗi project và một Version trên mỗi project. Các cột nguồn/chính sách bất biến có EF AfterSaveBehavior.Throw; không có API sửa/xóa snapshot. Lỗi tranh chấp được trả 409 khi ghi nguồn, worker kiểm tra lại ở vòng sau.

Không backfill các dự án Completed cũ chưa từng có lượt cấp 1 trong đợt này. Migration không sửa kết quả case/report cấp 2 trước đây.

## Worker và trạng thái

Tái sử dụng `VerificationBackgroundWorker`; thêm vòng xử lý với PostgreSQL advisory lock 70107. Vòng completeness và moderation đầu vào giữ nguyên.

| Trạng thái | Ý nghĩa |
| --- | --- |
| Pending | Snapshot đang chờ xử lý |
| Processing | Worker đã nhận lease và lưu attempt trước khi gọi provider |
| Passed | Các kiểm tra bắt buộc về report text/URL đạt trong phạm vi chính sách; không bao gồm nội dung file |
| Rejected | Có vi phạm từ cấm, ngữ cảnh hoặc URL; các kết quả chi tiết xác định nguồn |
| NeedsEvidence | Không có TaskEvidence hoặc thiếu report text cần kiểm tra; không cấp kết quả an toàn |
| Error | Chính sách/provider/lần xử lý lỗi hoặc kiểm tra chưa hoàn tất; không tự chuyển thành Passed |

SupersededAt độc lập với trạng thái lịch sử; mapping IsCurrent=false cho lượt đã bị thay thế. Kết quả đang chạy chỉ được áp dụng nếu lease token và nguồn/chính sách vẫn hiện hành. Kết quả đến muộn được giữ trong attempt với SOURCE_SUPERSEDED, không áp dụng lên phiên bản mới.

Retry dùng lại quy ước hiện có: tối đa 5 attempt, backoff 1/2/4/8 phút. Timeout, lỗi kết nối, HTTP 408/429/5xx và phản hồi provider không hợp lệ được xem là lỗi tạm thời. Lỗi cấu hình hoặc HTTP khác dừng retry tự động. Worker gửi thông báo cho Admin qua `VerificationNotifications` khi hết lượt hoặc cần xử lý lỗi thường trực.

Lease 10 phút dùng lại khoảng phục hồi Processing của moderation hiện có. Worker chết/cancel để lại attempt chưa kết thúc; khi hết lease, lần thử đó được ghi WORKER_LEASE_EXPIRED và xử lý lại theo budget/backoff. Lease token ngăn worker cũ ghi đè kết quả sau phục hồi. Provider có thể nhận lại request sau crash; không hứa exactly-once cho lời gọi mạng, nhưng không cấp hai kết quả hiện hành.

Kết quả từng mục chứa task/report/evidence ID, loại kiểm tra, URL, trạng thái, lý do, provider, thời điểm, proof và mã lỗi/transient. FileContent luôn là NotChecked và ghi rõ ngoài phạm vi; chỉ loại này được loại khỏi điều kiện Passed theo phạm vi người dùng đã chốt. Lỗi ở một kiểm tra bắt buộc làm lượt Error kể cả mục khác đã đạt hoặc phát hiện vi phạm; tất cả kết quả đã nhận vẫn được lưu.

## Cấu hình bắt buộc

Không cài sẵn danh sách từ cấm, ngưỡng AI, loại threat hoặc quy tắc chuẩn hóa nghiệp vụ. Người vận hành cung cấp cấu hình chính sách có phiên bản và khóa provider trước khi sử dụng. Không đặt khóa thật vào tài liệu hoặc source control.

| Khóa | Yêu cầu |
| --- | --- |
| EvidenceModeration:PolicyVersion | Chuỗi phiên bản do nghiệp vụ quản lý; đổi khi sửa chính sách hoặc chủ động kiểm tra lại sau sửa cấu hình/key |
| EvidenceModeration:ContentInstructions | Quy tắc kiểm duyệt ngữ cảnh bằng Gemini; ghi rõ các trường hợp được phép/cấm |
| EvidenceModeration:ForbiddenTerms | Mảng từ/cụm từ cấm literal, không rỗng; mọi match theo cấu hình bị từ chối, không có ngoại lệ ngữ cảnh ngầm cho danh sách này |
| EvidenceModeration:TermMatchMode | Chọn Substring hoặc WholeWord một cách tường minh |
| EvidenceModeration:TextNormalization | Chọn FormC hoặc StripDiacritics; StripDiacritics bỏ combining marks và ánh xạ đ/Đ sang d/D |
| EvidenceModeration:CaseSensitive | Bắt buộc bool tường minh; không có giá trị mặc định |
| Gemini:ApiKey | Khóa Gemini hiện có |
| Gemini:Model | Dùng cấu hình/model mặc định đã có trong provider cũ |
| SafeBrowsing:ApiKey | Khóa được bật Google Safe Browsing |
| SafeBrowsing:ThreatTypes | Danh sách threat types được nghiệp vụ lựa chọn theo API v4 |
| SafeBrowsing:PlatformTypes | Danh sách platform types được lựa chọn theo API v4 |

Environment/.env dùng `__` thay `:` và chỉ số mảng, ví dụ `EvidenceModeration__ForbiddenTerms__0`. Các trường chính sách được chụp bất biến; API key không nằm trong snapshot. Sau lỗi cấu hình/key thường trực, sửa cấu hình và tăng PolicyVersion để tạo lượt mới; không reset hoặc xóa lịch sử attempt cũ.

WholeWord dùng ranh giới chữ/số Unicode và underscore. ForbiddenTerms là quy tắc literal theo lựa chọn cấu hình; Gemini xét các quy tắc ngữ cảnh còn lại trong ContentInstructions. Không dùng điểm confidence hay completeness threshold để quyết định cấp 1.

Thiếu chính sách không làm hỏng thao tác Completed: snapshot vẫn được ghi và worker lưu Error. Thiếu evidence ưu tiên NeedsEvidence, không gọi provider. URL không hợp lệ/không được Safe Browsing hỗ trợ là Error, không mặc định an toàn.

URL checker chỉ gửi yêu cầu đến endpoint cố định của Google; HTTP handler không tự redirect. Không request URL evidence hoặc truy cập đích liên kết. Giữ proof phản hồi, và tuân thủ cacheDuration cho threat match. Không có match chỉ nghĩa là không nằm trong các danh sách đã chọn tại lần tra cứu, không phải bảo đảm tuyệt đối về website/file. Tham khảo [Lookup API v4](https://developers.google.com/safe-browsing/v4/lookup-api).

## Migration

`20261017075614_AddProjectEvidenceModeration` thêm hai bảng cấp 1, các index/constraint và mở rộng VerificationNotification để trỏ tới case cấp 2 hoặc lượt cấp 1 (đúng một trong hai). Notification hiện có giữ nguyên VerificationId; notification cấp 1 có EvidenceModerationId và VerificationId=null.

Migration chưa được áp dụng vào database thực tế trong lần triển khai code này. Rollback loại bỏ dữ liệu cấp 1 và notification của cấp 1 trước khi khôi phục VerificationId bắt buộc; cần lưu dữ liệu audit nếu dự định rollback sau khi đã vận hành.

## Kiểm thử và giới hạn

- Test snapshot, report version, task Cancelled, rollback giao dịch, idempotency, source/policy đổi, giữ lịch sử sau revision/delete, trường bất biến, lease phục hồi, retry budget và thông báo.
- Test từng nguồn vi phạm, dữ liệu provider sai/thiếu, HTTP errors, hủy request, key thiếu, cache threat, scope file và hồi quy completeness bằng HTTP handler giả lập.
- Test PostgreSQL tùy chọn dùng `UNINET_TEST_POSTGRES`, tạo schema ngẫu nhiên riêng rồi dọn đúng schema đó; không tự dùng connection string của ứng dụng. Khi biến chưa có, các test này được Skip và không được coi là đã kiểm tra concurrency thực tế trên PostgreSQL.
- Các provider chưa được gọi bằng key thật. Cần cấu hình chính sách/key và kiểm tra tích hợp trước khi vận hành.
- Giai đoạn 3 trở đi chưa triển khai: chưa có endpoint đọc/resubmit cấp 1; chưa chặn case cấp 2 theo kết quả cấp 1. Revision bằng API task report hiện có tự tạo lượt mới, còn thao tác resubmit cấp 1 riêng để giai đoạn sau.

## Các nhóm file thay đổi

- Domain: `ProjectEvidenceModeration`, `EvidenceModerationAttempt`, trạng thái và target notification.
- Infrastructure: DbContext, configuration của cấp 1/notification, migration và model snapshot.
- Application: DTO/mapping; source snapshot, checks, tasks, Safe Browsing checker; mở rộng provider Gemini và interface; nối thao tác Completed/ghi task; thông báo Admin.
- API: đăng ký dependency và vòng worker. Không bổ sung route.
- Tests: `EvidenceModerationTests`, `EvidenceProviderTests`, `EvidenceModerationPostgresTests`. Sửa tối thiểu lời gọi constructor sai sẵn có trong `PortfolioHttpTests` để test project biên dịch được, không sửa hành vi production của portfolio.

Kết quả chạy test cụ thể được ghi trong báo cáo hoàn tất; không coi việc build thành công là bằng chứng đã tích hợp provider hay áp dụng migration.

## Kết quả kiểm tra ngày 17/10/2026

- Build backend: thành công. Warning CS9113 về tham số `auth` chưa dùng trong ProjectsController đã tồn tại ở baseline.
- Lần chạy cuối nhóm ProjectVerification (bao gồm test cấp 1), EvidenceProvider, TaskReport, ProjectTask, GeminiModeration và Transaction service: **169 passed, 0 failed, 2 skipped**. Hai test skip là concurrency trên PostgreSQL vì chưa có UNINET_TEST_POSTGRES.
- EF `migrations has-pending-model-changes`: không có thay đổi model chưa được migration ghi nhận.
- `git diff --check`: không phát hiện lỗi whitespace.
- Một lần chạy mở rộng trước đó có hai lỗi ngoài thay đổi cấp 1: DatabaseDesignTests.SkillsUseExactlyFourCategories kỳ vọng constraint Skill khác model hiện có; TransactionHttpTests.AllFiveRoutesBindAndUserRoutesRequireStudentWhileWebhookAllowsAnonymous còn kỳ vọng route reconcile đã bị bỏ trong code cũ. Không sửa model Skill hoặc khôi phục route thanh toán để làm test qua.
- Test PortfolioHttpTests.PortraitHttpProcessConfirmOwnershipAndSaveRoundTrip được chạy riêng cùng nhóm hồi quy sau khi sửa constructor: vẫn lỗi HTTP 500 thay vì 400 tại nhánh partner portrait. Chỉ sửa lời gọi constructor để project test biên dịch; chưa xử lý lỗi runtime ngoài phạm vi này.
- Chưa kiểm tra provider với key thật, chưa chạy test PostgreSQL thực tế và chưa áp dụng migration lên database.
- Lệnh xuất SQL migration bị từ chối quyền thực thi; không chạy lại bằng cách khác, không có file SQL được tạo. Migration C#/Designer/model snapshot vẫn có đầy đủ.

## Danh sách file cụ thể

| Nhóm | File thêm hoặc sửa |
| --- | --- |
| Domain | `UniNet.Domain/Entities/ProjectEvidenceModeration.cs`; `UniNet.Domain/Enums/EvidenceModerationStatus.cs`; `UniNet.Domain/Entities/VerificationNotification.cs` |
| Infrastructure | `UniNet.Infrastructure/Data/UniNetDbContext.cs`; `UniNet.Infrastructure/Data/ProjectEvidenceModerationConfiguration.cs`; `UniNet.Infrastructure/Data/VerificationAdminActionConfiguration.cs` |
| Migration | `UniNet.Infrastructure/Data/Migrations/20261017075614_AddProjectEvidenceModeration.cs`; file Designer cùng tên; `UniNetDbContextModelSnapshot.cs` |
| Contract/mapping | `UniNet.Application/DTOs/Projects/EvidenceModerationContracts.cs`; `UniNet.Application/Interfaces/IEvidenceContentReviewer.cs`; `UniNet.Application/Interfaces/IVerificationAiReviewer.cs`; `UniNet.Application/Mappings/EvidenceModerationMapping.cs` |
| Service | `UniNet.Application/Services/EvidenceModerationSource.cs`; `EvidenceModerationChecks.cs`; `EvidenceModerationTasks.cs`; `GoogleSafeBrowsingUrlChecker.cs`; `GeminiVerificationAiReviewer.cs`; `ProjectService.cs`; `ProjectTaskService.cs`; `VerificationNotifications.cs` |
| API/worker | `UniNet.API/Program.cs`; `UniNet.API/Services/VerificationBackgroundWorker.cs` |
| Tests | `UniNet.Tests/EvidenceModerationTests.cs`; `EvidenceProviderTests.cs`; `EvidenceModerationPostgresTests.cs`; `PortfolioHttpTests.cs` |
| Tài liệu | `docs/project-evidence-moderation.md`; `docs/verification-background-tasks.md` |

`docs/project-verification-gap-plan.md` đã có từ lượt lập kế hoạch trước, không chỉnh nội dung file đó trong lần implement này.
