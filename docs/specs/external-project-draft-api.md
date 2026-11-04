# External project drafts: API, queue and confirmation

The backend implements the upload/review flow with four Student-only endpoints. Apply both
`AddUploadedFilesAndExternalProjectDrafts` and `AddExternalProjectConfirmation` migrations before deployment.
This repository contains the backend; the two frontend screens consume the contracts below.

## Upload screen

1. Upload files with `POST /api/images`; collect the returned `fileId` values.
2. Call `POST /api/external-project-drafts` with:

```json
{ "fileIds": ["uploaded-file-guid"] }
```

The response is **202 Accepted**, contains `draftId`, and has a `Location` header for GET.
It also returns the initial draft response. IDs must be distinct, owned by the active Student,
with 1–10 files including at least one raw PDF/DOCX/TXT. Other uploaded attachments can remain evidence.
The draft and its durable queue state are saved in the same database transaction implicit in SaveChanges.

## Review screen

Poll `GET /api/external-project-drafts/{id}`. The response includes:

- `draftId`, `status`, `version`, creation/update/expiry timestamps, `errorCode`, `confirmedProjectId`;
- `data`: editable project fields, initially populated from the AI extraction;
- `extraction`: original extraction and citations, kept separately from user edits;
- `files`: uploaded files available for selection as evidence;
- `missingFields`: names of fields required before confirmation.

Statuses follow the existing numeric JSON enum convention: Processing=0, Ready=1, Failed=2,
Confirmed=3, Expired=4. Drafts expire after seven days. Confirmed drafts remain readable/replayable.
Other users receive 404. Responses disable caching.

`PATCH /api/external-project-drafts/{id}` replaces the editable `data` property as one form object;
omitted fields inside `data` are not merged with earlier values. Send the latest server `version`:

```json
{
  "version": 3,
  "data": {
    "title": "UniNet",
    "description": "A completed software project",
    "projectField": "Software engineering",
    "startDate": "2024-01",
    "endDate": "2024-06",
    "submitterRole": "Backend Developer",
    "submitterContribution": "Designed and implemented the assessment APIs",
    "submitterIsLeader": false,
    "leaderName": "Declared external leader",
    "repositoryUrl": "https://github.com/example/project",
    "demoUrl": null,
    "skillIds": ["active-catalog-skill-guid"],
    "evidenceFileIds": ["uploaded-file-guid"],
    "features": ["Project assessment"],
    "members": [{ "name": "Declared contributor", "role": "Designer" }]
  }
}
```

Dates preserve YYYY / YYYY-MM / YYYY-MM-DD precision, must not be in the future, and must not
clearly end before they start. Evidence IDs must be a subset of this draft's owned files.
Skills must exist and be active. Incomplete forms may be saved; all required fields are checked on confirm.
At least one skill and one evidence file are required. A non-leader must declare the leader's name.
Processing/confirmed drafts are locked (409); stale versions return 409; expired drafts return 410.
A Failed draft may be manually corrected with PATCH, which makes it Ready without claiming AI success.
The original extraction/error remains available for context.

## Confirm

`POST /api/external-project-drafts/{id}/confirm`:

```json
{ "version": 4, "confirmSharingRights": true }
```

The backend validates the current form, ownership, active skills and explicit sharing permission.
One Serializable transaction creates the project, its skills/evidence, confirms the draft and creates
the initial Pending level-1 moderation case. Failure rolls back the whole transaction.

Response: `draftId`, `projectId`, `evidenceModerationId`, `version`, `origin`, `status`.
Confirmation replay returns the same IDs even with the original version. Concurrent confirmation uses
the draft concurrency token and PostgreSQL conflict handling; a losing attempt returns the confirmed
project if available, otherwise 409. No internal completion workflow, tasks, contribution cycle or
automatic membership records are created.

## Project ownership and recruitment

- `Origin`: Internal=0 (default for existing projects), External=1.
- `CreatorId` remains the platform record owner, authorized contact and requester/payer for assessment.
  It does not establish leadership of an external project.
- `SubmitterIsLeader` and `ExternalLeaderName` store the user's declaration. For an external leader
  outside UniNet, `leader.userId` and `leaderId` are null; no account is fabricated.
- Assignment and verification project responses include `ownerUserId`, `origin`, `leader` and `externalDetails`.
  General project detail/list responses include `origin` and `leader`; detail includes external data.
  Internal projects retain the creator-as-leader behavior.
- Imported projects are private, Completed and recruitment Closed. `MemberTarget`, `MinimumQuantity`
  and `RecruitmentDeadline` are nullable and null for external projects; internal values remain required.
- Implementation dates are stored separately as precision-preserving strings. `CreatedAt`/`CompletedAt`
  record import/confirmation time, not an invented historical completion timestamp.
- Submitter role/contribution and `SharingRightsConfirmedAt` are persisted. Declared members remain text,
  separate from platform membership. Project member APIs label the submitter with the declared role.

## Durable processing

The existing `VerificationBackgroundWorker` runs draft tasks under PostgreSQL advisory lock 70110.
`VerificationWorker:Enabled` controls the worker; `VerificationWorker:PollSeconds` controls polling
(default 60 seconds). The worker processes up to five drafts per pass. No in-memory queue or fire-and-forget task is used.

Per-draft leases last ten minutes. Claiming work increments Version and AttemptCount atomically.
Stale workers cannot publish over a newer claim/edit. Transient extraction errors retry after one/two
minutes, up to three attempts; permanent errors stay Failed. Expired leases are reclaimed after a crash.
An interrupted third attempt becomes Failed with `EXTRACTION_RETRY_EXHAUSTED`. Expiry invalidates outstanding work.
PATCH remains disabled during Processing, so extraction cannot overwrite an in-progress user edit.

## Direct project evidence and level 1

`ProjectEvidenceDocuments` references the selected `UploadedFiles` with a unique project/file pair.
Level-1 snapshots now support `projectEvidence` containing submitted project text and the document metadata.
No fake tasks/reports are inserted. Checks apply the existing text policy and URL reputation checks;
file content remains explicitly NotChecked, preserving the existing scope.

Direct results have null task/report IDs and, where applicable, `projectEvidenceDocumentId`.
Changes to direct source metadata/text produce a different source hash and invalidate an earlier decision.
The new snapshot property is omitted for internal projects, preserving legacy internal snapshot hashes.

Rollback of the schema is blocked while external projects exist to avoid converting inapplicable null
recruitment fields into misleading internal-project values.
