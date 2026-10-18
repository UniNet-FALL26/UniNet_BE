# Project verification — level 1 and level 2 (A + B)

## Public contracts

- `GET /api/projects/{projectId}/evidence-moderation`: latest level-1 decision, version,
  currentness, reasons, source/policy snapshots, attempts and per-item results.
- `POST /api/projects/{projectId}/evidence-moderation/retry`: no request body or uploaded
  evidence. Creator or active Admin only, as for the read route. A Completed project with
  no prior review can explicitly request its initial review; this never marks it Passed.
  Changed task/report data or policy creates a new immutable version. Pending/Processing
  requests are idempotent. An unchanged completed decision returns `LEVEL1_UNCHANGED`.
  Error retries retain the worker's five-attempt budget and scheduled backoff; exhausted
  or permanent errors require source/policy correction (`LEVEL1_RETRY_EXHAUSTED`).
- `POST /api/projects/{projectId}/verifications`: body `{ "packageId": "..." }`.
  Requires project creator, active account, Completed project, no Pending case, valid
  package and a current Passed level-1 decision matching both source and policy hashes.
  No evidence is submitted separately for level 2.
- The level-2 evidence submit/resubmit and AI completeness routes have been removed.
  Detail responses expose `evidenceModeration` instead of `evidenceSubmissions`/`aiPreCheck`.
  OpenAPI is generated from these controllers/contracts. Frontends must remove their
  verification upload/pre-check flows and use the level-1 read/retry routes.

Level 1 checks report text and URL reputation only. It does not read file contents.
It remains independent of purchasing a level-2 package. Project creation moderation,
completion contributions, report-group selection and soft-delete history remain intact.
Gemini content moderation and Google Safe Browsing remain registered; the completeness
worker and its provider contract are removed.

## Pinned decision and changes after creation

New cases persist `EvidenceModerationId`, a restrictive FK to the exact Passed review.
Booking, slot reads, checkout and assignment (including automatic replacement) recompute
source/policy hashes and reject stale or superseded decisions. A newly Passed version
never rebinds an existing case. As approved for this batch, these failures preserve the
case, original snapshot, booking and recorded payments; they do not initiate a refund.
Existing deadline/refund policies continue independently. There is no automatic case
recovery/rebinding API in this batch.

Creator case detail, Admin case detail and authorized verificator assignment detail load
that pinned snapshot using their existing case permissions. This grants no general task
or report access. An historical snapshot remains readable after supersession.

## Booking and assignment

A + B retain the existing payment-before-booking sequence; reservation and payment order
changes belong to C. Slots and booking require a successful linked payment for the case's
requester/package with no Pending/Success refund and no duplicate-payment-review flag.
Assignment requires the same payment eligibility and a valid future booking, in addition
to current level 1 and existing staff membership/conflict rules.

Supply approved `ProjectVerification` settings: `TimeZoneId`, `WorkingDays`,
`WorkdayStart`, `WorkdayEnd`, `SlotMinutes`, `Capacity`, `MinimumNoticeMinutes`,
`MaxAdvanceDays`. Missing/invalid policy returns `MEETING_POLICY_NOT_CONFIGURED` (503).
`RequireEvidenceOnCreate` and `RequiredEvidenceTypes` are no longer used.

## Migration and historical data

`LinkVerificationToEvidenceModeration` only adds a nullable UUID, index and restrictive FK.
It does not drop or rewrite legacy evidence, AI completeness columns, report versions,
Admin actions, notifications or payments. Legacy cases are not automatically linked or
marked Passed: they remain readable, but booking/checkout/assignment returns
`LEVEL1_LINK_REQUIRED` until a separately reviewed migration handles them. Never infer a
historical decision by attaching the latest Passed review. This migration is generated,
not applied to an application database by this change.

Mutations retain Serializable transactions and write-conflict handling. Read-only slot
availability is advisory; booking checks eligibility/capacity again in its transaction.
Provider callbacks still record actual money, including for a case whose source changed;
level-1 eligibility must not hide an actual successful payment.

## Verification of this batch (18 October 2026)

- API/test projects compile. EF reports no pending model changes, and the incremental
  migration SQL was generated successfully. No application database was migrated.
- Expanded selection: 220 passed, 2 failed, 2 skipped (224 total). It includes A/B,
  level-1/provider fakes, checkout, deadline/refund, contribution, task/report, portfolio,
  payment HTTP and database model tests. No portrait/native ONNX tests were selected.
- Two existing contract mismatches, verified against the unchanged HEAD sources:
  `TransactionHttpTests.AllFiveRoutesBindAndUserRoutesRequireStudentWhileWebhookAllowsAnonymous`
  calls the absent `/api/payments/{id}/reconcile` route; `DatabaseDesignTests.SkillsUseExactlyFourCategories`
  expects a non-null category check although the model permits null categories. These
  failures were not repaired by changing unrelated product behavior in A/B.
- Two PostgreSQL concurrency tests skip because `UNINET_TEST_POSTGRES` is not configured.
  Real PostgreSQL migration/concurrency and real Gemini/Safe Browsing/PayOS integrations
  are not verified by these results. Provider tests use fakes/HTTP handlers.
