# Remove project classification and show recent update

## Completed 2026-11-17
- Removed `Project.Origin`, its enum/check/default, API origin fields and computed Hub mode. Historical migrations remain unchanged.
- Preserved existing creator leadership before dropping Origin; imported leader/details and assessment history remain intact.
- FE and dashboard no longer show Hub project type/source or personal/community badges. Creation, recruitment and friend invitations use status/membership rules.
- Project information shows API `updatedAt` in Vietnam time with hours/minutes; saving refreshes it immediately and reloading preserves it.
- Updated obsolete dashboard enum test and browser fixture; added backend timestamp persistence/read regression test.
- Isolated PostgreSQL runner now supports Windows PowerShell's cryptography API.

## Database
- Applied `20261117092115_RemoveProjectClassification` to configured Neon database after isolated migration verification and backup.
- Before/after project count: 10. No Origin or Mode column remains.
- Backup: `artifacts/project-classification/before-removal-20261117-verified.dump` (790851 bytes, readable pg_restore inventory).
- Reviewed SQL: `artifacts/project-classification/remove-classification.sql`.
- Rollback requires restoring backup: removed classifications cannot be reconstructed.
- API deployments must use the updated code because older versions still query Origin.

## Verification
- Focused backend project/schema/membership tests: 41 passed, including timestamp regression.
- Isolated PostgreSQL migration tests: 2 passed; evidence `artifacts/verification-g1/20261117T094519977Z/g1-postgres.trx`.
- FE hub service/timestamp tests: 8 passed. Dashboard tests: 11 passed; production build passed.
- Playwright `tests/project-links-browser-check.cjs` against port 8090 passed: removed labels, timestamp after save/reload, validation/retry, owner permissions and responsive modal checks.
- Full backend run: 670 passed, 49 skipped, 4 failures in SkillsUseExactlyFourCategories and partner registration/name tests outside this change. PostgreSQL tests were subsequently run separately as above.
- FE typecheck remains blocked by unrelated opportunities.tsx symbol/styles errors and AssessorProfilePosts.tsx CommentModal props mismatch.

## Follow-up
- Resolve unrelated backend test and FE typecheck failures before a full release.
- No deployment was performed.
