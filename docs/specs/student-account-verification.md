# Student account verification

## Scope and acceptance
- Keep existing authentication, profile, partner verification, and `UserVerifications` schema.
- Active Student submits legal name, any college/university name (both 2–255 characters), student code (3–50 letters/numbers or `. _ / -`), one JPG/PNG/PDF up to 5 MiB. Backend validates required text, allowed code characters, extension, MIME and signature.
- No identity-based automatic approval. Active Admin reviews; rejection needs a reason. Retain every rejected/approved row when resubmitting.
- At most one pending Student request per account. Concurrent review is conditional on Pending. Resubmission cooldown: 60 seconds since previous submission.
- Status comes from the backend. Sensitive bytes are available only to the owner or active Admin via authenticated download.

## Contract
- `GET /api/student/verification/me`: status.
- `POST /api/student/verification`: multipart `fullName`, `schoolName`, `studentCode`, `document`; returns status.
- Status: `{id,status,isVerified,fullName,schoolName,studentCode,submittedAt,reviewedAt,rejectReason,documentName}`. `status`: null/unsubmitted, 0/Pending, 1/Approved, 2/Rejected. Times are UTC ISO timestamps.
- `GET /api/student/verification/{id}/document`: owner/Admin, attachment, `private,no-store`, `nosniff`; no public URL in any response.
- `GET /api/admin/student-verifications?status=0&page=1&pageSize=20`: `{page,pageSize,totalItems,items:[{id,userId,email,verification:<status>}]}`; omit status for history.
- `GET /api/admin/student-verifications/{id}`: status for any historical Student request.
- `POST /api/admin/student-verifications/{id}/approve`: returns status.
- `POST /api/admin/student-verifications/{id}/reject`: JSON `{reason}`, returns status.
- Error shape follows existing `{code,message}`. Pending/already verified/reviewed: 409. Cooldown: 429, `VERIFICATION_RATE_LIMITED`. Invalid fields/document/reason: 400. Unauthenticated: 401. Wrong role/owner/inactive: 403.

## Implementation increments
1. DTO + failing state/privacy/validation tests.
2. Student service, authenticated multipart/document routes, admin routes, database locking, conditional review.
3. Reuse notification entity with additive nullable UserVerificationId FK and exactly-one-target check migration.
4. Real JWT/HTTP multipart and concurrent submission/review tests; API build and relevant/full backend suite.

## Persistence and consistency
- Existing `UserVerifications.VerificationData` JSON stores validated snapshot and private base64 document; `DocumentUrls` stays null. No local disk persistence or public cloud upload. This increases JSON size by about one third (max 6.7 MiB/request) and stores sensitive document data in database backups; secure database/backup access is required. Query responses map explicit DTOs and never expose raw snapshot JSON.
- PostgreSQL transaction advisory lock keyed by account serializes Student submit/review across app instances. Conditional review update prevents stale double decisions. SQLite fixtures use serializable write transactions.
- Approval writes the reviewed name/school/code atomically to the profile with `IsVerified=true`. Existing profile edits may invalidate verification; an Approved historical row then maps status null while retaining its metadata, and can be resubmitted. History is preserved.
- Reuse `VerificationNotification`, adding nullable `UserVerificationId` FK/index and extending exactly-one-target check. Existing project/evidence notifications remain valid. Existing notifications API permits active Students and always filters own recipient ID. Student event keys are `student-verification:{id}:submitted|approved|rejected`.

## Deployment and limits
- Generate/review only: `AddStudentVerificationNotifications`; do not apply to a real database during development. Deploy migration before enabling these endpoints. No data deletions in Up. Down explicitly refuses rollback before schema changes while student notifications exist; preserve/migrate notification history before removing that FK column.
- Tests exercise SQLite and HTTP. A real PostgreSQL database is needed to verify advisory locking and migration application in production-equivalent conditions. No live account or document is used by tests.
