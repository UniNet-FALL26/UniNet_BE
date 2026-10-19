# G0 — Kiểm kê và chuẩn bị bảo toàn dữ liệu

Ngày thực hiện: 19/10/2026 (Asia/Bangkok).

## Phạm vi và nguồn

Thực hiện riêng G0 theo `D:\project-verification-entity-cleanup-plan-2026-10-18.md`, tham chiếu `docs/project-verification-gap-plan.md`, `docs/project-verification-final-report.md`, source/migration hiện tại. Tài liệu `D:\project-verification-next-plan-2026-10-17.md` được dẫn chiếu nhưng không có trên máy; không coi đã đọc tài liệu đó.

Người dùng xác nhận database dùng trong DBeaver là database Neon cấu hình trong `.env` của dự án. Nhãn kiểm kê: `configured-neon`. Không suy rộng kết quả sang database/môi trường khác chưa được cung cấp. Không áp migration, sửa dữ liệu nguồn, dừng dịch vụ triển khai hoặc thực hiện G1/G2.

## Migration thực tế

Đọc trực tiếp `public.__EFMigrationsHistory`: **36 migration**, khớp toàn bộ 36 migration source (không tính Designer và ModelSnapshot), không còn migration chờ trong source tại thời điểm kiểm kê.

| Migration cần xác nhận | Kết quả database |
| --- | --- |
| `20261017152032_AddStudentVerificationNotifications` | Đã áp |
| `20261018003641_LinkVerificationToEvidenceModeration` | Đã áp |
| `20261018033831_AddVerificationSlotReservations` | Đã áp |
| `20261018041023_SingleVerificationReportAndRefundAudit` | Đã áp |

## Kiểm kê thực tế

| Bảng | Số hàng |
| --- | ---: |
| ProjectModerations | 6 |
| VerificationNotification | 1 |
| ProjectEvidenceModerations | 0 |
| EvidenceModerationAttempts | 0 |
| VerificationAdminAction | 0 |
| VerificationReportVersions | 0 |
| ProjectVerifications | 0 |
| VerificationAssignments | 0 |
| VerificationReports | 0 |
| ReportAuditLogs | 0 |
| RefundTransactions | 0 |
| VerificationTransactions | 0 |

- Case đã pin EvidenceModerationId: **0**; case finalized: **0**.
- Report nhiều revision hoặc nhiều report trên một case: **0**; không có dữ liệu để đối chiếu backfill canonical/finalizer/LegacyOriginalJson thực tế.
- Refund Admin/System/LegacyUnknown: **0/0/0**; không có dữ liệu để chứng minh backfill actor lịch sử.
- Notification lịch sử trỏ cấp 1: **0**. Giữ nguyên notification hiện có và luồng xác minh sinh viên.
- ID trùng giữa hai bảng moderation: **0**.
- Rà catalog constraint/FK thực tế: **112 FK**, **0 kiểm tra orphan thất bại**, **0 constraint chưa validated**. Có danh sách đầy đủ FK inbound/outbound, composite keys, columns và indexes trong bằng chứng.
- Lưu danh sách ID và MD5 toàn bộ nội dung mỗi hàng của 12 bảng trên theo thứ tự ID; checksum SHA-256 ở cấp tệp. MD5 chỉ dùng đối chiếu hàng, không dùng xác thực mật mã.

## Archive và phục hồi

Chọn **full database custom-format pg_dump**, giữ schema, dữ liệu và các phụ thuộc người dùng/project/payment, không chỉ export JSON một số trường. Không di chuyển bảng active sang archive schema ở G0.

Bộ bằng chứng riêng tại:

`D:\SEP490\UniNet_BE\artifacts\verification-g0\configured-neon-20261019T015051469Z`

Thư mục `artifacts/` đã được gitignore. Backup chứa dữ liệu người dùng, không đưa vào Git hoặc gửi công khai. Vị trí này là bản lưu cục bộ phục vụ G0; trước phát hành cần chuyển bộ archive sang kho backup được quản lý, hạn chế quyền đọc, kiểm tra checksum sau chuyển và ghi nhận người quản lý/retention. Chưa xác nhận chính sách kho triển khai.

- `database.dump`: **361344 byte**.
- SHA-256: `5134B0830BAEA81B41939F14EF3A2E03AA82785C1C0C644BF3D98412D3F8737F`.
- `archive-toc.txt`: danh sách đối tượng từ pg_restore.
- `inventory.jsonl`: 136 section gồm metadata, migrations, row keys/fingerprints và các kiểm tra liên kết.
- `sha256.json`: checksum/size của bằng chứng ban đầu.
- `restore-verification.json`: kết quả restore và đối chiếu, đọc trường RestoreVerified trước khi sử dụng.
- `restore-*/`: log và cluster thử nghiệm cô lập, được dừng sau kiểm tra. Dữ liệu gốc/bằng chứng thô được giữ nguyên.

Phép thử dùng PostgreSQL 18 tại localhost, cluster mới có mật khẩu ngẫu nhiên, không ghi database Neon. `pg_restore --exit-on-error --no-owner --no-privileges` khôi phục schema+dữ liệu; owner/grants vẫn nằm trong archive nhưng việc áp quyền/role triển khai chưa được kiểm chứng. Đối chiếu 135 section ngoài metadata định danh database; chuẩn hóa catalog tên database tạm, đánh số vị trí cột liên tục sau khi bỏ khoảng trống của cột đã xóa (giữ nguyên thứ tự cột), và duy nhất CHECK `CK_ContributionResults_Scores` khi token của các so sánh AND giữ nguyên, chỉ khác ngoặc do deparser. Các khác biệt khác làm kiểm tra thất bại.

Inventory dùng transaction Repeatable Read Read Only; pg_dump dùng snapshot riêng. Đối chiếu với bản restore xác nhận dữ liệu trong phạm vi kiểm kê khớp hai thời điểm. Đây chưa phải snapshot cutover khi đã đóng writer. Không dùng backup cũ này làm điểm rollback cho các giao dịch phát sinh sau đó.

## Writer freeze, cutover và rollback

G0 chưa chuyển đổi dữ liệu nên **không dừng writer đang phục vụ người dùng**. Không có bằng chứng deployment đã dừng; `SourceWritersStopped=false`, `CleanupAllowed=false` là chủ đích. Khi bước vào G1/G2 phải thực hiện đủ các bước sau:

1. Ghi lại phiên bản ứng dụng, instance, scheduler/worker và người vận hành; chặn ghi ở ingress và dừng mọi instance cũ, gồm luồng student verification nếu backup/cutover cần điểm nhất quán toàn database.
2. Tắt `VerificationWorker:Enabled` (biến môi trường `VerificationWorker__Enabled=false`) trên tất cả instance và restart theo quy trình triển khai. Cờ này chỉ tắt worker, **không chặn API writer**. Dừng các writer moderation submission/evidence, case, assignment, report, payment/refund và callback liên quan; bảo đảm webhook được lưu/retry theo quy trình nhà cung cấp, không mất sự kiện thanh toán.
3. Xác nhận request/transaction và lease đang chạy đã kết thúc, kiểm tra session DB theo instance/version. Không dùng advisory lock của worker như bằng chứng tất cả API đã ngừng ghi. Không tự force-kill transaction tài chính.
4. Chạy lại inventory+full backup trong thời gian freeze; kiểm tra log không lỗi, checksum, TOC, restore cô lập và count/ID/hash/FK trước/sau. Archive thiếu, restore thất bại, collision, orphan hoặc sai hash thì dừng cleanup.
5. Chỉ chạy migration G1/G2 đã được kiểm chứng riêng. Không đồng thời cho hai phiên bản ghi hai bảng moderation. So sánh lại toàn bộ pinned IDs, attempts, notification, snapshots/hash/policy và history.
6. Rollback ưu tiên restore **toàn bộ database** từ điểm freeze đã kiểm chứng vào database sạch, cấu hình roles/grants, đối chiếu lại rồi trỏ ứng dụng tương thích tới database phục hồi. Không chồng restore lên database đang ghi, không restore vài bảng bỏ mất FK/phụ thuộc, không chạy ứng dụng mới trên schema cũ.
7. Sau khi mở writer, restore điểm cũ có thể mất giao dịch mới; phải dừng và đối soát payment/refund/webhook trước khi quyết định. Migration Down cleanup tương lai phải khôi phục lịch sử trước D/E Down hoặc từ chối rõ ràng; G0 không tạo Down giả/tạo bảng lịch sử rỗng.

## Công cụ tái sử dụng

Chạy từ thư mục repository bằng PowerShell và PostgreSQL client 18 đã cài:

```powershell
./scripts/verification-g0/Invoke-Inventory.ps1 -EnvironmentName configured-neon -Archive
./scripts/verification-g0/Test-ArchiveRestore.ps1 -EvidenceDirectory '<thu-muc-bang-chung-vua-tao>'
```

Kết nối lấy từ biến môi trường `ConnectionStrings__UniNet`, rồi mới `.env`; không dùng mật khẩu fallback và không in connection string. Giữ VerifyFull/Channel Binding; CA được xuất từ Windows trusted root store cho libpq. Lỗi kết nối/query/backup/restore phải dừng. Không đưa secret lên command line. Muốn kiểm kê môi trường khác phải cung cấp kết nối riêng qua biến môi trường và nhãn môi trường riêng.

## Điều kiện còn lại trước G1/G2

- Restore thực tế đã đạt lúc **08:56:40 ngày 19/10/2026**: `RestoreVerified=true`, **135 section khớp**, không còn khác biệt ngoài các chuẩn hóa đã ghi. PostgreSQL tạm đã dừng.
- Chuyển archive vào kho được quản lý và xác nhận quyền/retention/roles/grants triển khai.
- Thực hiện writer freeze thật và backup mới tại cutover; hiện mới có runbook, chưa có bằng chứng dừng deployment.
- Chuẩn bị fixture PostgreSQL có history không rỗng, nhiều revisions/actions, pinned cases, refunded/paid/assigned/finalized cases và conflict IDs để kiểm chứng migration G1/G2. Database hiện tại không có các trường hợp đó, nên restore G0 không chứng minh logic chuyển đổi lịch sử.

G0 cung cấp baseline database thật, archive và phép kiểm chứng khôi phục; chưa cấp phép cleanup hay nghiệm thu migration G1/G2.

