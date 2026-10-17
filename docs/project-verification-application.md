# Project verification Application service

`IProjectVerificationService` is registered in the existing API dependency injection container.
All methods accept the authenticated **account ID**, resolve the profile and check the current
`Project.CreatorId`. Active accounts are required. No controllers/endpoints or migrations are added.

## Workflow

- `CreateAsync`: Completed project, owning leader, no Pending request, active verification package
  matching the account role. The selected package is recorded, but no charge is created here.
  Initial evidence is optional unless `RequireEvidenceOnCreate` is enabled. `SubmittedAt` on the
  verification records the request time; each evidence submission has its own timestamp.
- `GetMyVerificationsAsync`: only projects currently led by the caller; optional project/status
  filters, page >= 1 and page size 1..100.
- `GetDetailAsync`: project, all evidence versions, AI feedback, meeting times, assignment reports
  and actual payment/refund attempts. Empty collections/nulls represent data not yet available.
- `SubmitEvidenceAsync`: first submission. `ResubmitEvidenceAsync`: a new immutable version of the
  complete evidence set. To supplement evidence, send both retained and new items in that version.
  Evidence requires a type, title and absolute HTTPS URL; duplicate URLs and missing configured
  types are rejected. Links are stored, not downloaded or interpreted as commands.
- `GetAiPreCheckAsync`: reads persisted feedback without invoking the existing content moderation
  provider or finalizing verification. `HasResult=false` means no feedback yet. `IsCurrent=false`
  means the result is not associated with the latest evidence version. The future pre-check writer
  must persist `AiSummary`, `AiScore`, `AiReviewedAt`, `AiEvidenceVersion` and
  `AiAdditionalEvidenceJson` (a JSON string array) together after validating provider output.
  There is no existing evidence pre-check integration; project content moderation is a different
  operation. This service does not invent AI thresholds or return simulated results.
- `GetAvailableMeetingSlotsAsync` and `ScheduleMeetingAsync`: require submitted evidence and a
  linked Success transaction for the selected package and requester, with no Pending/Success
  refund attempt. Refunded charges cannot authorize a new booking. Working hours, notice and horizon are
  checked on the server. Capacity is the smaller of configured capacity and active verificator
  profiles. Existing overlapping bookings consume capacity; the same leader cannot double-book
  across projects. Updating a booking excludes its own existing reservation.
- Evidence changes, scheduling and cancellation require Pending, no finalization, no assignment,
  no started/ended meeting, and a future existing booking if one exists.
- `CancelAsync` records the reason/time and changes only verification state. It does not infer
  refund eligibility or change transaction status. Cancellation frees the booking's capacity.
- `GetFinalResultAsync`: reports or Verified/Failed result must exist; otherwise returns a 404
  business exception. Reports are linked to their assignments and all previous reports remain
  visible. No leader operation can set Verified or Failed.

No existing completed-project peer-review eligibility policy was found. Creation checks Completed,
package eligibility and configured evidence requirements; further prerequisites must be specified
before adding them. Configured evidence types are enforced on every evidence submission.

## Required meeting configuration

Add approved values under `ProjectVerification` in deployment configuration. The following values
are an **example to review**, not defaults automatically installed by this change:

```json
{
  "ProjectVerification": {
    "RequireEvidenceOnCreate": false,
    "RequiredEvidenceTypes": ["demo", "source"],
    "TimeZoneId": "Asia/Bangkok",
    "WorkingDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "WorkdayStart": "09:00:00",
    "WorkdayEnd": "17:00:00",
    "SlotMinutes": 60,
    "Capacity": 2,
    "MinimumNoticeMinutes": 1440,
    "MaxAdvanceDays": 30
  }
}
```

Missing/invalid meeting configuration returns `MEETING_POLICY_NOT_CONFIGURED` (503). A query range
may span at most 31 days. Slot boundaries are generated in the configured timezone and all stored
and returned timestamps are UTC. Nonexistent/ambiguous daylight-saving times are skipped.
Availability represents the configured common working calendar and persisted bookings; the
current schema has no staff-specific leave/working-hours calendar. Assignment must independently
check the selected verificator's eligibility and conflicts.

## Persistence and concurrency

Minimal schema support was added for evidence versions, selected package, AI feedback metadata,
cancellation reason/time and a nullable finalizer for Pending requests. All mutations use
Serializable transactions. A filtered unique index enforces one Pending verification per project;
evidence versions are unique within a verification. PostgreSQL serialization, unique-key and
deadlock conflicts are converted to `VERIFICATION_CONFLICT` (409); reload and retry the operation.

**No migration was generated or applied.** The service requires a later migration for these model
changes before use against an existing PostgreSQL database. Existing rows also need an explicit
package backfill when making `PackageId` required. Read/payment/admin/AI writers should use the
same transaction discipline; this service does not implement payment providers, assignments,
background jobs or formal final decisions.
