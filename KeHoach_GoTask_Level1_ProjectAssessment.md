# Kế hoạch bỏ Project Verification AI (Level 1), giữ Project Assessment

Ngày lập: 04/11/2026. Dự án: UniNet. Căn cứ: mã nguồn backend tại `D:\SEP490\UniNet_BE`.

Đây là kế hoạch triển khai, chưa thay đổi code nghiệp vụ hoặc database. Tên bảng dưới đây lấy từ EF Core model hiện tại; chưa kết nối database triển khai để xác nhận dữ liệu thực tế. Công việc frontend là đầu việc phối hợp, chưa kiểm tra repository frontend.

## 1. Phạm vi và luồng đích

| Hạng mục | Quyết định triển khai |
|---|---|
| Project Task | Giữ nguyên quản lý task, phân công, task report, review và evidence gắn task; chỉ tháo các hook gọi AI verification level 1. Không xóa API, bảng hoặc dữ liệu task. |
| Verification Level 1 | Bỏ kiểm duyệt evidence bằng AI, kiểm tra URL thuộc luồng này, retry, snapshot/hash và điều kiện phải pass level 1. |
| Verification Level 2 | Đổi tên nghiệp vụ thành **Project Assessment**; giữ đặt lịch, gói dịch vụ, thanh toán, phân công verificator, buổi họp, rubric, báo cáo, kết quả, hủy/hoàn tiền và lịch sử. |
| Minh chứng assessment | Giữ minh chứng phục vụ người đánh giá, bao gồm task/report/evidence hiện có khi phù hợp và demo/repository/tài liệu. Chỉ bỏ bước AI kiểm duyệt cùng điều kiện pass level 1; không bắt buộc thiết kế lại nguồn minh chứng. |
| Kiểm duyệt dự án khi đăng | Giữ `ProjectModerationPurpose.ProjectSubmission = 0` và AI moderation đang phục vụ đăng dự án. |
| Xác minh Student/Partner | Giữ nguyên; không đổi tên hàng loạt mọi thành phần có chữ Verification. |
| Contribution | Giữ nguyên bảng, API, công thức, trọng số, snapshot, lịch sử và việc đọc task/report để tính điểm. Không đổi formula version hoặc tạm dừng tính điểm trong đợt này. |

Luồng đích: dự án **Completed** → tạo yêu cầu **Project Assessment** → chọn/giữ lịch và thanh toán theo cơ chế hiện hành → phân công người đánh giá → họp/đánh giá theo rubric → kết quả và báo cáo. Không có bước AI pass level 1 hoặc FK sang evidence moderation. Giữ các điều kiện phân quyền, gói dịch vụ và chống trùng hồ sơ hiện tại.

## 2. Bảng kế hoạch triển khai

Ước lượng theo ngày công (NC), không phải cam kết lịch. Vai trò: BE = backend, FE = frontend, QA = kiểm thử, PO = người chốt nghiệp vụ. Tất cả đầu việc đang ở trạng thái **Chưa thực hiện**.

| ID | Ưu tiên | Công việc cụ thể | Thành phần chính | Phụ thuộc | Phụ trách | NC dự kiến | Điều kiện hoàn thành |
|---|---|---|---|---|---|---|---|
| P01 | P0 | Xác nhận phạm vi chỉ bỏ AI verification level 1; phân loại hồ sơ đang chờ/đã trả tiền/đã kết thúc. | Dữ liệu assessment và evidence moderation | — | PO + BE | 0,5 | Task và Contribution giữ nguyên; có cách xử lý từng nhóm hồ sơ; không tự hủy hoặc hoàn tiền do bỏ level 1. |
| P02 | P0 | Chốt tên Project Assessment, route/DTO và cách trình bày minh chứng cho người đánh giá sau khi bỏ EvidenceModeration khỏi response. | ProjectVerificationContract; ProjectAssessmentContracts; ProjectVerificationEvidence | P01 | BE + FE + PO | 0,5–1 | Có mẫu response và mapping route nếu đổi; giữ rubric, ngưỡng, kết quả và nguồn task evidence đang dùng. |
| P03 | P0 | Kiểm kê dữ liệu AI level 1, backup và thử phục hồi; thống kê FK và thông báo tham chiếu. | PostgreSQL; migration; Notifications | P01 | BE | 0,5–1 | Có bản backup phục hồi được và số liệu đối chiếu; task, evidence và Contribution không thuộc danh sách xóa. |
| P04 | P0 | Gỡ điều kiện level 1 xuyên suốt vòng đời; chuyển VerificationEligibility thành policy assessment không truy vấn ProjectModerations. Giữ Completed và các guard nghiệp vụ còn lại. | ProjectVerificationService; VerificationEligibility; AdminProjectVerificationService; AssignmentService; TransactionService; VerificationDeadlineTasks | P02 | BE | 1–1,5 | Tạo hồ sơ, xem lịch, đặt lịch, trả tiền, callback và phân công chạy khi dự án không có bản ghi level 1; không còn lỗi LEVEL1_REQUIRED/STALE/LINK_REQUIRED. |
| P05 | P0 | Tách helper dùng chung khỏi lớp level 1 trước khi xóa: write-conflict detection và hash nếu còn caller. Rà cả module task được giữ lại. | EvidenceModerationSource; ProjectTaskService; ReportService; TransactionService; VerificatorAvailabilityService; ProjectService; ProjectVerificationService | P04 | BE | 0,5 | Các service giữ lại không tham chiếu lớp sắp xóa; concurrency của task/report và assessment vẫn giữ hành vi cũ. |
| P06 | P0 | Gỡ API/service/DTO/mapping riêng cho evidence moderation; chỉ bỏ hook RefreshAsync tạo AI snapshot trong ProjectService và ProjectTaskService. | EvidenceModerationController; EvidenceModerationService/Tasks/Checks/Source; EvidenceModerationContracts/Mapping; ProjectService; ProjectTaskService | P04, P05 | BE | 0,5–1 | Hoàn tất dự án và CRUD task/report không tạo Purpose=1; toàn bộ nghiệp vụ task/report/evidence vẫn chạy. |
| P07 | P0 | Gỡ DI, client và cấu hình chuyên dùng level 1; bỏ riêng worker 70107. Giữ worker 70106 cho kiểm duyệt đăng dự án và toàn bộ tác vụ đặt lịch/tài chính. | Program.cs; VerificationBackgroundWorker; GeminiVerificationAiReviewer; IEvidenceContentReviewer; IEvidenceUrlChecker; GoogleSafeBrowsingUrlChecker; cấu hình EvidenceModeration/SafeBrowsing | P06 | BE | 0,5 | Không còn chạy nền/call provider level 1; AI kiểm duyệt đăng dự án và các worker khác vẫn chạy. Chỉ xóa config/client sau kiểm tra không còn consumer. |
| P08 | P0 | Kiểm tra bảo toàn Contribution; không thay đổi cách tính, nguồn task/report hoặc schema. | ContributionService; bộ tính điểm; contracts/entities | P06 | BE | 0,5 | Cùng input cho cùng điểm và formula version; lịch sử đọc được, tính mới vẫn hoạt động. |
| P09 | P0 | Kiểm tra bảo toàn module task sau khi tháo hook AI: CRUD, assignment, report draft/submit/revise/withdraw/delete, review và upload evidence. | ProjectTasksController; ProjectTaskService; ProjectTaskStatusResolver; task contracts/entities | P05, P06 | BE | 0,5 | API, phân quyền, concurrency, version report và dữ liệu giữ nguyên; không tạo AI moderation. |
| P10 | P1 | Gỡ EvidenceModeration khỏi response/include và target thông báo chuyên level 1; cập nhật thông báo assessment. Giữ target task và quyền dùng chung. | ProjectVerificationMapping; AssignmentService; AdminProjectVerificationService; NotificationTargets; NotificationPermissionPolicy | P02, P04, P06 | BE | 0,5–1 | Không còn link level 1 trong thông báo mới; thông báo task và quyền assessment vẫn hoạt động. |
| P11 | P1 | Đổi tên phía người dùng thành Project Assessment, bỏ nhãn Level 1/2; cập nhật route/DTO theo P02. Khuyến nghị giữ tên bảng và ID assessment trong đợt này. | Controllers/interfaces/contracts; Swagger; FE | P02, P10 | BE + FE | 1–2 | FE không gọi level 1; màn hình/API task và Contribution giữ nguyên; không đổi ID hay giá trị enum lưu trữ. |
| P12 | P0 | Migration chỉ gỡ FK, dữ liệu và cấu trúc chuyên AI level 1; cập nhật model snapshot và guard SaveChanges. | UniNetDbContext; UnifiedProjectModerationConfiguration; migrations | P03, P04, P06–P10 | BE | 1–1,5 | Upgrade/dựng DB mới thành công; không drop hoặc sửa bảng task/Contribution; Purpose=0 và hồ sơ assessment còn nguyên. |
| P13 | P0 | Viết lại test eligibility, thay test level 1 bằng test assessment độc lập; hồi quy tài chính, rubric, phân quyền, Contribution và migration PostgreSQL thực. | UniNet.Tests; MigrationFixtures | P11, P12 | BE + QA | 1–1,5 | Các ca ở mục 5 đạt; không còn test runtime yêu cầu pass AI; lịch sử migration cũ vẫn có thể replay. |
| P14 | P0 | Diễn tập triển khai trên bản sao database, so sánh số liệu và chạy luồng đầu-cuối với FE; triển khai đồng bộ, theo dõi callback, job và lỗi. | staging → production; runbook | P13 | BE + FE + QA | 0,5–1 | Không mất tiền/hồ sơ; không lỗi thiếu bảng/cột; có phương án quay lui đã thử. |

Tổng dự kiến **9–14 ngày công**, gồm phối hợp FE/QA và kiểm tra bảo toàn Task/Contribution, chưa tính thời gian chờ chốt hợp đồng FE. P08/P09 là kiểm tra hồi quy, không phải thiết kế lại hoặc gỡ hai module này. Không drop cấu trúc AI trước khi mọi consumer được xử lý.

## 3. Bảng xử lý database

| Bảng / cấu trúc hiện tại | Hướng xử lý | Điều kiện và ghi chú |
|---|---|---|
| TaskEvidence | Giữ nguyên | Giữ metadata, tệp và FK vào TaskReports; bỏ AI verification không đồng nghĩa xóa evidence. |
| TaskReportReviews | Giữ nguyên | Giữ review nghiệp vụ của task report, không thuộc AI verification. |
| TaskReports | Giữ nguyên | Giữ draft, version, self-reference, trạng thái, soft delete và nguồn dữ liệu Contribution. |
| ProjectTaskAssignments | Giữ nguyên | Giữ phân công task và liên kết user. |
| ProjectTasks | Giữ nguyên | Giữ schema, API, dữ liệu, navigation; chỉ tháo hook AI khỏi service. |
| EvidenceModerationAttempts | Xóa bảng | Lưu lịch sử cần thiết, gỡ quan hệ restrict tới ProjectModerations trước. |
| ProjectModerations, các hàng Purpose=1 | Xóa dữ liệu level 1 | Chỉ sau khi tháo FK từ ProjectVerifications và bảng attempts. KHÔNG drop toàn bảng. |
| ProjectModerations, các hàng Purpose=0 | Giữ | Đây là kiểm duyệt dự án khi đăng, không phải level 1 evidence. |
| ProjectVerifications.EvidenceModerationId và shadow column EvidenceModerationPurpose | Xóa hai cột cùng FK, index liên quan và CK_ProjectVerifications_EvidencePurpose | Kiểm tra tên constraint thực tế từ migration/model; giữ nguyên ID và trạng thái hồ sơ. |
| ProjectModerations: Version, SourceHash, SnapshotJson, PolicyHash, PolicyJson, SupersededAt, CompletedAt, LeaseExpiresAt, LeaseToken, NextRetryAt, AttemptCount, LastErrorCode, LastError, Summary | Ứng viên gỡ cột chuyên evidence | Rà toàn bộ consumer trước khi chốt; gỡ index Purpose=1, evidence snapshot check và concurrency/immutability guard tương ứng. Không nhầm AttemptCount với AttemptNumber/RetryCount dùng cho moderation đăng dự án. |
| ProjectModerations.Purpose và alternate key (Id, Purpose) | Đơn giản hóa sau khi tháo mọi FK composite | Có thể giữ Purpose chỉ nhận 0 trong đợt đầu để giảm thay đổi; nếu gỡ thì cập nhật ProjectModerationService, filter, contract, index và tests trong cùng đợt. |
| ProjectVerifications: AiSummary, AiScore, AiReviewedAt, AiEvidenceVersion, AiAttemptEvidenceVersion, AiAttemptCount, AiNextRetryAt, AiLastError, AiAdditionalEvidenceJson | Kiểm tra dữ liệu/consumer; gỡ nếu chỉ là legacy AI | Không suy luận chỉ từ tên cột. Chuyển minh chứng hoặc dữ liệu lịch sử còn cần trước khi drop; không dùng AI làm điều kiện assessment mới. |
| ProjectVerificationEvidence | Giữ có điều kiện / tái sử dụng làm minh chứng assessment | Là snapshot nộp minh chứng riêng của hồ sơ; không phải TaskEvidence hoặc EvidenceModerationAttempts. Chốt tại P02; chỉ drop nếu xác nhận không còn nghiệp vụ, dữ liệu cần lưu đã chuyển và không còn consumer. |
| ProjectVerifications; VerificationAssignments; VerificationReports; ReportAuditLogs | Giữ | Là lõi Project Assessment. Giữ AssessmentRubricJson, AssessmentJson, outcome và lịch sử báo cáo. Đổi tên vật lý không bắt buộc để đổi thuật ngữ nghiệp vụ. |
| VerificationTransactions; Transactions; RefundTransactions; Packages; VerificatorAvailabilities | Giữ | Giữ liên kết và ID; package type/enum có thể giữ giá trị lưu trữ hiện tại dù tên hiển thị đổi. |
| ContributionCycles/Participants/PeerEvaluations/PeerEvaluationHistory/CalculationRuns/Results/Reviews | Giữ nguyên | Giữ tính mới, nguồn task/report, snapshot, FormulaVersion, trọng số, TaskExecutionScore, ApprovedDeliverablesScore và kết quả lịch sử. |
| Notifications và cấu hình quyền liên quan | Giữ bảng, chỉ sửa target AI level 1 | Giữ notification task; không xóa quyền EvidenceReview nếu ReportService hoặc admin assessment còn dùng. |

## 4. Thứ tự migration và triển khai

1. Chốt phạm vi chỉ gỡ AI level 1 và hợp đồng assessment; thống kê dữ liệu, backup và diễn tập restore. Task và Contribution tiếp tục được giữ nguyên.
2. Hoàn thiện code không còn phụ thuộc level 1, đặc biệt tháo hook trong ProjectTaskService nhưng giữ toàn bộ xử lý task/report. Ngừng phát sinh AI level 1 và dừng worker 70107 trên mọi instance.
3. Nếu triển khai cuốn chiếu, bảo đảm phiên bản cũ đã ngừng chạy trước bước destructive. Ưu tiên cửa sổ bảo trì ngắn, triển khai đồng bộ API/worker/FE cho đồ án; không để binary cũ truy vấn cột/bảng đã xóa.
4. Bảo toàn minh chứng/hồ sơ lịch sử; tháo composite FK assessment → moderation, bỏ attempts, xóa riêng moderation Purpose=1; sau đó mới gỡ cột/index/check dành riêng level 1.
5. Không drop hoặc sửa schema TaskEvidence, TaskReportReviews, TaskReports, ProjectTaskAssignments, ProjectTasks và Contribution. Kiểm tra migration không có thao tác ngoài phạm vi; không dùng DROP CASCADE để che giấu phụ thuộc.
6. Tạo **migration mới**, cập nhật EF snapshot; không xóa/sửa migration cũ đã áp dụng. Với database mới, chạy được toàn bộ lịch sử rồi migration gỡ bỏ.
7. Kiểm tra số lượng/ID hồ sơ, assignment, báo cáo, giao dịch, hoàn tiền, task/report/evidence, Contribution và moderation Purpose=0 trước/sau; chạy smoke test rồi mở lại dịch vụ.

Rollback: Down migration chỉ dựng lại schema, không thể phục hồi dữ liệu đã drop. Quay lui đầy đủ cần backup và phiên bản code tương thích; đối soát giao dịch/callback phát sinh sau backup trước khi restore. Không tạo payment/refund trùng khi chạy lại callback hoặc job.

## 5. Bảng kiểm thử nghiệm thu

| Nhóm | Tình huống bắt buộc | Kết quả mong đợi |
|---|---|---|
| Assessment độc lập | Completed project không có task hoặc level 1; level 1 cũ fail/stale/null | Tạo assessment và thực hiện các bước hợp lệ mà không đọc trạng thái level 1. |
| Điều kiện còn giữ | Dự án chưa Completed; không phải leader; gói sai role/inactive; đã có hồ sơ Pending | Vẫn bị từ chối đúng policy hiện hành. |
| Booking | Giữ lịch, hết hạn, tranh chấp cùng slot, hủy và đặt lại | Không double booking; không có lỗi thiếu FK level 1. |
| Thanh toán | Checkout, webhook thành công/trùng/trễ; callback cho hồ sơ tạo trước migration | Idempotent; không mất giao dịch và không sinh thêm thu/hoàn tiền do bỏ level 1. |
| Phân công và lịch họp | Assign/accept/reject/reassign, timeout, phòng họp | Giữ phân quyền, thời hạn và giới hạn hiện tại; không còn pin moderation. |
| Rubric | Đúng/sai version, thiếu tiêu chí/điều kiện, sát ngưỡng 65, insufficient evidence | Giữ rubric version 1.0, trọng số và cách tính hiện tại; kết quả do assessment, không do AI level 1. |
| Lịch sử | Hồ sơ cũ có/không có EvidenceModerationId, đã trả tiền, đã có final report | Xem và xử lý theo trạng thái hiện tại; không yêu cầu migration review level 1. |
| Moderation giữ lại | Đăng dự án và retry AI moderation Purpose=0 | Chạy bình thường; không bị xóa client/config hoặc dữ liệu liên quan. |
| Project Task | CRUD, assignment, report draft/submit/revise/withdraw/delete, review, upload evidence | Nghiệp vụ, API, dữ liệu và concurrency giữ nguyên; không tạo snapshot hoặc chạy AI level 1. |
| Contribution | Tính mới từ cùng input task/report và peer evaluation; đọc lại run cũ | Điểm và formula version giữ nguyên; không bỏ nguồn task/report, không đổi trọng số hoặc dừng tính mới. |
| API/FE và thông báo | Bỏ màn hình/route level 1; kiểm tra màn hình task/Contribution và deeplink cũ | Task/Contribution giữ nguyên; không lỗi 500 hoặc target trái quyền; tên Project Assessment nhất quán. |
| Migration PostgreSQL | Upgrade database có dữ liệu; tạo DB trống từ migration đầu; thử phục hồi backup | Không lỗi FK/check/index; giữ đủ dữ liệu nghiệp vụ còn sử dụng. |
| Concurrency và background | Xung đột ghi, nhiều API instance, callback/job chạy lại | Helper dùng chung vẫn hoạt động; không chạy job 70107; job tài chính và moderation đăng dự án không bị tắt. |

Các test cần rà cụ thể: VerificationLevel1LinkTests, EvidenceModerationTests, EvidenceModerationPostgresTests, UnifiedModerationTests, UnifiedModerationMigrationPostgresTests, ProjectTaskTests, TaskReportTests và nhóm Contribution. Giữ/mở rộng ProjectAssessmentTests, ProjectVerificationTests, VerificationReservationTests/PostgresTests/PaymentTests, VerificationBackgroundTests, VerificationFinalReportTests, VerificationReportPostgresTests, VerificationHistoryArchivePostgresTests. Không xóa test migration lịch sử chỉ vì có nhắc tới bảng cũ.

## 6. Nguồn đối chiếu và điểm cần chốt

Các đường dẫn sau tương đối với `D:\SEP490\UniNet_BE`:

| Phát hiện | Nguồn mã nguồn |
|---|---|
| Gate pass level 1 và hash/pinned version | UniNet.Application/Services/VerificationEligibility.cs; ProjectVerificationService.cs |
| Gate lan sang tiền, phân công, admin và deadline | UniNet.Application/Services/TransactionService.cs; AssignmentService.cs; AdminProjectVerificationService.cs; VerificationDeadlineTasks.cs |
| Level 1 dùng dữ liệu task/report | UniNet.Application/Services/EvidenceModerationSource.cs; ProjectTaskService.cs; ProjectService.cs |
| Moderation dùng chung bảng, Purpose 0/1 và FK composite | UniNet.Infrastructure/Data/UnifiedProjectModerationConfiguration.cs; UniNetDbContext.cs; UniNet.Domain/Enums/ProjectModerationPurpose.cs |
| Worker evidence riêng và worker khác cần giữ | UniNet.API/Services/VerificationBackgroundWorker.cs; UniNet.API/Program.cs |
| Rubric, evidence snapshot và response | UniNet.Application/Services/ProjectAssessmentPolicy.cs; UniNet.Application/Mappings/ProjectVerificationMapping.cs; UniNet.Domain/Entities/ProjectVerification.cs; ProjectVerificationEvidence.cs |
| Contribution đọc trực tiếp task và report | UniNet.Application/Services/ContributionService.cs, CaptureInputSnapshotAsync |
| Notification và quyền dùng chung | UniNet.Application/Services/NotificationTargets.cs; ReportService.cs |

Phạm vi đã chốt theo cập nhật: **giữ Project Task và Contribution**, chỉ gỡ AI verification evidence level 1 và điều kiện phụ thuộc của assessment. Cần thống nhất với FE cách hiển thị minh chứng sau khi bỏ EvidenceModeration khỏi response, route mới nếu đổi và thời gian tương thích. Khuyến nghị giữ tên bảng assessment hiện có, đổi thuật ngữ bên ngoài trước. Không xóa AI moderation khi đăng dự án (Purpose=0).

