# Paid project verification without Level 1

Paid project verification no longer requires a passed, current or explicitly linked Level 1 evidence review. New verification requests do not create or pin an evidence moderation record.

The change applies to request creation, meeting slot lookup and reservation, checkout, successful payment confirmation, Admin booking recovery, assessor assignment/replacement, and overdue replacement eligibility. Meeting and report services already operate independently of evidence moderation; regression coverage now includes final reports for projects without any evidence review.

The remaining requirements are unchanged: active authorized accounts, project ownership, Completed project status, valid packages, the existing meeting reservation/calendar rules, payment matching, refund exclusions and assignment conflict checks. This change does not redesign booking or automatic replacement behavior.

## Existing data and API compatibility

Database cleanup is prepared in `20261108142140_ArchiveAndRemoveLevel1`. It archives historical records and links before removing active L1 storage. It has been tested only on isolated PostgreSQL databases and has not been applied to the application's live database. See [the deployment and rollback runbook](../operations/archive-remove-level1.md).

The `evidenceModeration` field has been removed from verification and assignment detail responses. External draft confirmation no longer returns `evidenceModerationId`. Clients must stop reading these fields and remove their Level 1 badges, retry buttons and prerequisite screens.

The standalone GET `/api/projects/{projectId}/evidence-moderation` and POST `/api/projects/{projectId}/evidence-moderation/retry` routes are removed and return 404. The evidence worker, services, providers, DI registrations, L1 DTOs and L1-only `EvidenceModeration`/`SafeBrowsing` configuration have been removed. Historical evidence notification targets are unavailable rather than routing to a retired review screen.

Project completion, task/report mutations and external draft confirmation no longer create or refresh evidence reviews. External-project extraction and selected document storage remain available. Project submission content moderation, prohibited-content checks and student/company account verification are unchanged.

The current EF model no longer contains the L1 attempt entity, L1 snapshot/provider columns or verification-to-L1 relationship. Existing migration files remain unchanged. Historical data is copied into a separate read-only archive by the new cleanup migration; live project moderation retains only the submission purpose. The shared permission key `verification.evidence.review` is retained because current Admin/Assessor report and assignment authorization still uses it; it no longer exposes an L1 function.

## Regression coverage

- Missing evidence records permit request creation, scheduling, checkout, callback confirmation, Admin recovery, assignment and report submission.
- Pending, Processing, Rejected, NeedsEvidence, Error, Passed and superseded legacy reviews do not gate paid verification.
- Changes to the old evidence policy after checkout do not invalidate payment confirmation.
- Historical evidence links and immutable review history are preserved.
- Unpaid requests still reject assignment; incomplete projects still reject scheduling/checkout and cannot confirm a booking from a payment callback.
- Duplicate callbacks do not create extra payment rows or assignments.

PostgreSQL-specific concurrency tests require a separately configured test database. No production database operation is part of this change.

## Prerequisite removal validation

- Focused verification/payment/meeting/assessment/moderation regression run: 192 passed, 0 failed.
- Full suite: 777 passed, 4 failed, 29 skipped (810 total). The skipped tests require the PostgreSQL test environment.
- The four failures were reproduced against an isolated archive of the unchanged HEAD: `DatabaseDesignTests.SkillsUseExactlyFourCategories`, two `AuthTests.InvalidPartnerNamesDoNotCreateAnAccount` cases and `AuthTests.PartnerRegistrationSeparatesOrganizationAndPersonalNames`. They are pre-existing and outside this change.
- Results: `artifacts/remove-l1-tests/remove-l1.trx`, `remove-l1-full.trx` and `baseline-results/baseline-existing-failures.trx`.
- `git diff --check` passed.

## Feature removal validation

- Focused verification/payment/meeting/task/report/notification/external-project regression run: 252 passed, 0 failed.
- Full suite after removing retired tests: 720 passed, 4 failed, 27 skipped (751 total). The failed test names match the four unchanged-HEAD failures above. The remaining skipped tests require PostgreSQL configuration.
- HTTP regression checks confirm both retired evidence review routes return 404 and external draft confirmation no longer returns `evidenceModerationId`.
- Completion and task changes create no new L1 records and do not supersede historical reviews. Historical evidence notifications cannot resolve to retired review pages.
- External document confirmation and replay work without an L1 record; attachment insertion failures still roll back the project and draft confirmation atomically.
- No L1 runtime services, DTOs, providers, configuration or controller references remain in API/Application source. Database schema and old migrations were not changed.
- Results: `artifacts/remove-l1-tests/remove-l1-features.trx` and `remove-l1-features-full.trx`.

## Database cleanup validation

- New PostgreSQL migration tests: 10 passed, including all L1 statuses, exact row/attempt/link round trips, read-only archives, tampered/missing archive refusal, unknown dependencies/triggers, empty databases, changed relationships and a real custom-format pg_dump/pg_restore round trip.
- Wider isolated PostgreSQL run: 20 passed and 7 failed. The additional passing cases include booking concurrency. All 7 failures also reproduce on the unchanged HEAD: the older notification/unified-moderation fixtures seed an older schema using the current account model, which expects a missing `SessionVersion` column. These failures occur before this migration is applied.
- Current-schema backend regression group: 204 passed, 0 failed.
- Full suite without PostgreSQL configuration: 710 passed, the same 4 pre-existing failures, 37 skipped (751 total). PostgreSQL coverage is reported separately above.
- Build succeeded with 0 errors. EF reports no pending model changes after the new snapshot.
- Review SQL scripts were generated without connecting to the application database: `artifacts/remove-l1-tests/archive-remove-l1-up.sql` and `archive-remove-l1-down.sql`.
- PostgreSQL evidence: `artifacts/verification-g1/20261108T143804429Z/g1-postgres.trx`; old-fixture baseline: `artifacts/remove-l1-tests/baseline/artifacts/verification-g1/20261108T144049462Z/g1-postgres.trx`.
- Current-model evidence: `artifacts/remove-l1-tests/l1-schema-regression.trx` and `l1-schema-full.trx`.
