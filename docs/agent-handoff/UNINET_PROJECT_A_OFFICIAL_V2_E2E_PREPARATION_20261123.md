# UNINET — PROJECT A OFFICIAL CONTRIBUTION V2 E2E PREPARATION

2026-11-23 — audit/preparation only. No project completion, snapshot/cohort/rating/run/publication, sync, migration, reset or live consent performed. No production code changed; existing worktree retained.

## Current verified state

Database identity read-only: `127.0.0.1:55433/uninet_github_fe_e2e_20261121`, role `uninet_test`, container `uninet-step2-concurrency`. No root `.env`, Neon or original E2E connection. API PID 19808, health 200, runtime worker-off receipt verified.

Project A `44b9e967-a830-4061-b33b-5d96ca41df07` remains Active (1), CompletedAt null, workflow OfficialV2 already selected. StartDate 2026-11-22, StartDateIsCreationDate=true, ActualEndDate null. A/B identities are verified; latest identity history `2026-11-23T03:22:54.010439Z`. Both current associations Active, linked, grants unrevoked, installation Active/VerificationUnknown=false. Historical unlinked mln131 row remains retained. No active GitSyncJob. No Project A Git snapshot or Peer cohort exists. Coverage table exists; no migration is needed for preparation.

## 1. Finish Project from existing FE

Current source flow:
- [ProjectSettingsPanel](../../../UniNet_FE/src/components/projects/hub/ProjectSettingsPanel.tsx): Cài đặt -> Quản lý dự án -> Hoàn thành, visible to owner for non-Completed/non-Rejected projects.
- [ProjectHubDetailScreen](../../../UniNet_FE/src/components/projects/hub/ProjectHubDetailScreen.tsx): confirmation dialog -> service.complete -> refresh saved project response.
- [FE service](../../../UniNet_FE/src/services/project-hub.service.ts): **POST `/api/project-hub/{projectId}/complete`**, no body.
- [Controller](../../UniNet.API/Controllers/ProjectHubController.cs) -> [ProjectHubService.CompleteAsync](../../UniNet.Application/Services/ProjectHubService.Mutations.cs).

Requirements: authenticated Active Student Creator, project exists and is not Rejected; row locked inside transaction. The Hub path does not require Git/Peer/Tasks to be complete before completion. For current Active A, the source permits completion. It validates actual-end >= start when both are known. With current creation-date mode and empty actual end, completion populates actual end via the application's ProjectDay convention; the current start is compatible with today's completion date.

Effects: Project.Status=Completed, CompletedAt set if missing, UpdatedAt changed; ProjectDetails completion metadata updated; creation-date mode may fill ActualEndDate; project/recruitment status closed, pending **application** requests expired. Invitation requests are not expired by the applicationsOnly branch. Membership and Git associations/identity history/evidence remain intact. No Git sync, automatic capture, Peer cohort, score or Run is called. This path does not enqueue completion notifications explicitly.

**Important separate API:** legacy PATCH `/api/projects/status` invokes ProjectService.UpdateStatusAsync with a stricter Active -> Completed transition. It expires applications/closes recruitment and queues notifications; it creates a legacy ContributionCycle only when OfficialV2 is not selected. Project A is OfficialV2, so that path would also skip legacy Cycle creation. Current FE uses Hub POST, not this legacy PATCH. Do not combine or call both.

Completing does not enable OfficialV2 or automatically start it: A already has its persisted mode. Completion satisfies the project-state guard; Creator still explicitly performs each capture/Peer/run action.

## 2. Official Git capture contract

All official routes below use prefix:
`/api/projects/44b9e967-a830-4061-b33b-5d96ca41df07/contribution/v2`

Creator A must use own authenticated Student session. Delegated Git Manager capability alone does not authorize Official Contribution management.

POST `/snapshots`
```json
{"observationCutoff":"<UTC cutoff chosen at execution time, after 2026-11-23T03:22:54.010439Z and not in the future>"}
```
The placeholder is a contract illustration, not an executable request. Select cutoff at actual approved execution, not the earlier Git job completion timestamp; otherwise B's later verified identity history would be excluded from later attribution.

Official service requires Creator, OfficialV2, Completed, cutoff <= server time. Capture service uses PostgreSQL Repeatable Read, project manager authorization and observation/job/association/repository-version joins. It freezes header + exact membership + copied completed checkpoint receipts atomically, then finalizes. Existing immutable snapshots are not updated. Coverage table availability selects policy 2; official reader checks policy 2. Capture can retain historical associations for audit; authorization/integrity/scoring are separately checked on read, not inferred from matching shared repository IDs.

Current expected candidate scope, if no intervening changes: 49 observations / 38 versions from A's two current associations; 28 completed source checkpoints eligible for receipt capture. Exact capture membership is determined at its MVCC read point, not guaranteed by this audit. B association `25fe740d-dfbf-4859-8ce1-257247b4b3e7` must not appear. A's IDs are `5313bc39-8ffc-45a6-8e18-272cdc33a4a9` and `38135f80-f18e-48c9-a9f4-df73fe268d92`.

After capture: GET `/snapshots/{id}`, `/snapshots/{id}/memberships`, `/snapshots/{id}/readiness`. Check policy=2, RestrictedCount=0, exact association/observation/version identities, receipt counts, validator status/reasons, canonical integrity/input hash. Immutability is enforced by existing snapshot guards. A new snapshot cannot repair corrupt source facts or upgrade Incomplete facts. Review empty coverage must be verified from persisted receipts, not live counters.

Possible blockers: incomplete project state now; authorization lost at read time, bad cutoff/roster/history, unsupported/corrupt selected facts, restricted private evidence, missing/mismatched policy-2 receipts, concurrent conflicts. Existing 7 Incomplete audit/PR-derived entries do not alone imply retained scoring facts are invalid. No new schema blocker or required re-sync was found. No capture or integrity mutation test was run live.

## 3. Peer workflow

Official roster is all effective members including Creator:
- A `fef4ef4d-0049-4f7a-b629-fe9760bbbff9`.
- B `8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`.
- C `ee920f44-7e77-4e2f-9f79-82c4c146d170`.

POST `/peer-cohorts` body `participantIds` = the three profile IDs above. Official wrapper requires Creator, OfficialV2, Completed and exact effective roster. It opens an explicit immutable roster, not legacy Cycle participants. It does not require a Git identity for every participant; C participates normally.

Each member uses their own authenticated session:
PUT `/peer-cohorts/{cohortId}/evaluations/{evaluateeProfileId}`
Body fields: `score` integer 1..5, `comment` nullable, `expectedRevision` null/0 for first submission, exact current revision for edits. No rating values are selected in this preparation.

Required pairs: A->B,C; B->A,C; C->A,B (**6**). No self-review, no another-user impersonation. Unique pair constraints, revision checks and revision audit prevent duplicate/accidental overwrites. Closed cohort rejects changes. Ordinary member read is limited to own submitted ratings; Creator has the authorized management view. FE must not put private rating/comment values into public member rows.

GET `/peer-cohorts/{id}` verifies 6/6 and ReadyToClose. Creator POST `/peer-cohorts/{id}/close`, no body. Calculator requires every directed pair; inactive roster is rejected; PostgreSQL guards also validate completeness and snapshot existence. Close persists hashed immutable Peer Snapshot and marks Closed; repeated close returns existing snapshot. GET `/peer-snapshots/{id}` verifies policy/input hash/roster/state. No missing Peer value is converted to a fake zero; applicable zero-mass Peer remains InsufficientData.

Open-cohort and capture requests do not have the Run RequestId idempotency mechanism: read workflow/existing IDs after an ambiguous response before issuing another creation request.

## 4. Exact official API sequence (future authorized actions)

| Step | Request | Body / condition |
|---|---|---|
| Complete | POST `/api/project-hub/{A}/complete` | none; Creator confirmation |
| Recheck workflow | GET `{prefix}/workflow` | Completed=true, mode OfficialV2 |
| Capture | POST `{prefix}/snapshots` | observationCutoff |
| Inspect/readiness | GET `{prefix}/snapshots/{gitSnapshotId}` and `/readiness` | immutable policy-2 read; Git Ready, receipts verified |
| Open Peer | POST `{prefix}/peer-cohorts` | participantIds A+B+C |
| Read/submit | GET `{prefix}/peer-cohorts/{cohortId}`; PUT `.../evaluations/{evaluateeId}` | own session, score/comment/expectedRevision; six pairs |
| Close Peer | POST `{prefix}/peer-cohorts/{cohortId}/close` | none; Creator, complete ratings |
| Inspect Peer | GET `{prefix}/peer-snapshots/{peerSnapshotId}` | state Ready, integrity valid |
| Create Draft | POST `{prefix}/runs` | requestId, gitSnapshotId, peerSnapshotId |
| Review view | GET `{prefix}/runs/{runId}`, `/activities`, `/exclusions`, `/audit` | Creator only; verify hashes/limitations/results |
| Enter Review | POST `{prefix}/runs/{runId}/submit` | expectedRevision, optional feedback |
| Approve | POST `{prefix}/runs/{runId}/approve` | expectedRevision, optional feedback |
| Finalize | POST `{prefix}/runs/{runId}/finalize` | expectedRevision, optional feedback |
| Publish | POST `{prefix}/runs/{runId}/publish` | transition DTO; only Finalized, explicit publication |
| Member result | GET `{prefix}/results/me` | own authorized Published result only |

`prefix` is defined above. A new Run RequestId must be generated only when that action is authorized; reuse the same ID on ambiguous-response recovery. Use returned current Revision, not a guessed constant. Reject is supported with `/reject`, requiring feedback, followed by corrected resubmission. Review is a state/read activity, not the legacy experimental `/review` API. Official Creator-only review/approval rules do not require or invent an independent second reviewer.

Create validates exact effective roster, both frozen inputs and hashes, complete/positive applicable Git/Peer mass, official policy, source job completion, current authorization and no legacy finalized-result conflict. It does not allow incomplete inputs to become an official Draft score. Finalized inputs/results are not overwritten; publishing is separate and privacy checks protect unpublished calculations.

## 5. FE readiness and plan

Existing FE supports Project completion, repository management, self Git Identity, and own Published result label. It has **no Official V2 workflow/capture/readiness/Peer form/review lifecycle UI**. No V2 Peer submission controls were found.

Old Workspace routes are retired redirects to project list/detail; do not assume a functioning Contribution Workspace exists or revive a duplicate lifecycle. Suggested implementation in current Project Detail's project management/Đóng góp area (no GitHub tab):
1. Creator panel driven by GET workflow, blocked reasons and next supported action.
2. Capture/readiness view showing two repository associations, immutable IDs/hash/coverage and safe category limitations.
3. Member Peer form listing only the other two roster members, own drafts/revision conflict handling and submission progress; Creator-only close action.
4. Creator Draft/review panel with exclusions and state-specific submit/approve/finalize/publish confirmations.
5. Members tab continues to show only results/me Published response for the requesting member. Other members' scores require a separate authorized published-roster contract; do not reuse privileged Draft/Experimental results.

No FE/BE implementation or browser mutation was performed in this audit. Source-supported completion is not a claim of a newly exercised live click.

## 6. Next action after user completion

First **GET workflow and reverify current authorization/identities/cutoff**. Then, with explicit approval, Creator captures one policy-2 Git Snapshot and reads readiness. Do not open/submit Peer or create a Run automatically merely because project becomes Completed.

Current immediate blocker: ProjectNotCompleted. Git identity C is not a blocker. No migration or mandatory new Git sync is identified. Future immutable Git/Peer inputs and real evaluations remain required before Draft creation; partial-overlap/force-push limitations remain visible under existing policy.

Read-only DB/source audit and health checks completed; no automated test/build rerun (no source behavior changed). Runtime remains worker-off. No Project B mutation, irreversible operation or commit/push.
