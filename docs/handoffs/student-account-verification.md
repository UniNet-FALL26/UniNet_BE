# Student account verification handoff

- Spec/contract: `docs/specs/student-account-verification.md`.
- Reuses `UserVerifications`, `UserProfile.IsVerified`, existing JWT role authorization, AuthException response handling, VerificationNotification and own-recipient notifications endpoint.
- Added service/DTO/controller and Student Admin endpoints; existing Partner flow remains separate.
- Documents: private base64 in existing JSON metadata, max 5 MiB, JPG/PNG/PDF signature/MIME/extension checks, download owner/Admin only with attachment/no-store/nosniff. Never expose JSON snapshot or document URL.
- Pending duplicate prevention uses PostgreSQL transaction advisory lock per account; review conditional Pending update; 60-second resubmit cooldown. Rejected rows are retained. Approval atomically syncs reviewed identity to profile. Existing profile invalidation restores unverified status and permits resubmit.
- **Required before deployment:** apply generated additive `AddStudentVerificationNotifications` migration (nullable UserVerificationId FK/index and expanded notification target check). Not applied to any live DB by this task.
- Focused tests: StudentVerificationTests and StudentVerificationHttpTests cover validation, roles, cross-account access, expired JWT, private attachment headers, real multipart submit, concurrent duplicate submit/review, rejection, resubmit, approval, notification recipients, and invalidated profile re-verification.
- Actual PostgreSQL concurrency and deployment migration application remain unverified without an isolated PostgreSQL test database.
- Validation: 9 focused tests passed; full suite before the last validation-only additions had 445 passed, 5 failed, 2 skipped (452 total). Unrelated failures: 3 AuthTests partner names, DatabaseDesignTests.SkillsUseExactlyFourCategories (old check expectation), TransactionHttpTests.AllFiveRoutesBindAndUserRoutesRequireStudentWhileWebhookAllowsAnonymous (expected 200, got 404). API build succeeded; existing ProjectsController unread `auth` warning. Full suite logs: ignored `artifacts/student-verification-tests.log`.
