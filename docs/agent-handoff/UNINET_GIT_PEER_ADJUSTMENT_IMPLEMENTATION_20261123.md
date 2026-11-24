# UNINET — Git + Peer Contribution adjustment implementation

## Status
Implemented in source and disposable/fixture tests. **Not deployed to FE E2E**. No live Git fetch, enqueue, capture, Peer, Draft or publication was executed. Current API PID15640 remains on the prior binary, health200, workers off. Current Project A Published Run remains `00902016-dbf8-4fe6-9664-470d1bd2bc0b`, release1. See [source audit](./UNINET_COMBINED_ADJUSTMENT_SOURCE_AUDIT_20261123.md).

## Architecture and API
Existing Completed-project manual sync, GitSyncWorker, lease/heartbeat, generation fencing, provider, atomic writer, checkpoints, policy2 capture/reader, readiness, Peer and atomic approve-and-publish are reused. No new sync engine or scoring formula. Creator-only combined actions under `/api/projects/{projectId}/contribution/v2`:

| Method | Suffix | Effect |
|---|---|---|
| GET | `/adjustments/latest` | Project-authorized durable plan/status; schema absent returns unavailable |
| GET | `/adjustments/by-request/{requestId}` | Exact ambiguous-start reconciliation; never guesses latest |
| GET | `/adjustments/{adjustmentId}` | Exact project-owned plan |
| POST | `/adjustments` | `{requestId}`; creates one durable plan and exact Manual jobs atomically |
| POST | `/adjustments/{id}/git-snapshot` | Explicit atomic new policy2 capture + immutable plan link; replay returns exact ID |
| POST | `/adjustments/{id}/peer-round` | Ready owned Git input -> new effective-roster cohort, no copied ratings |
| POST | `/adjustments/{id}/run` | Closed owned Peer Snapshot + owned Git -> existing Official Draft service |
| POST | `/adjustments/{id}/abandon` | Explicit end before Peer opens; active valid lease blocks; pending/expired scoped jobs cancelled with fencing; all evidence/snapshots retained |

Existing evaluation PUT/close and `runs/{id}/approve-and-publish` remain. No automatic publish; actual revision/currentPublishedRunId are supplied. Repeated actions reconcile durable links. Backend owns selection; FE never substitutes workflow.latest inputs for an active combined plan. Existing APIs remain compatible and still have their original contracts; the stricter freshness gate applies to combined capture.

## Correlation/freshness
`GitContributionAdjustments` holds project, Creator, RequestId, baseline publication, deadline, state/revision and write-once Git/Peer/Run links. `GitContributionAdjustmentRepositories` freezes exact association/repository/Manual Job IDs. One active attempt per project and one plan owner per job. SQL guards prevent plan identity/item replacement/deletion, cross-project input substitution and incorrect run inputs. Failed capture rolls back snapshot/header membership/link together. Snapshot capture shares the outer Serializable transaction; standalone capture remains Repeatable Read and rejects a weaker ambient isolation.

Start resolves every linked association (1..20), rejects paused/unlinked/AccessLost/PendingAuthorization/non-App authorization and active competing jobs; restoration uses existing separate access/activation flows. All repositories are required, no silent skip. Provider repository read verification occurs in the existing processor before evidence writes; enqueue success alone is not proof of remote access. Runtime default off blocks start, not falsely reports success.

Capture requires each exact job Completed with completed root/checkpoint scopes, timestamps and evidence when items were reported. Branch and PR/Issue child scope checkpoints are checked using frozen identities/facts; no mutable payload is used as scoring history. Later relevant jobs or changed linked scope block capture. Recheck/authorization row locks and Serializable read point serialize against revocation; concurrent writes after capture's read point cannot retroactively enter immutable membership. All historical project observations remain retained according to capture policy, including unlinked audit history; readiness/current read authorization can still block restricted facts.

Raw observation count and newly observed version count are explicitly not eligible contribution counts. After capture, existing diagnostic selection/overlap policy compares scored category/repository/object/owner/actor/date/version-content/relations against the baseline's verified frozen calculation: Ready+equal -> NoEligibleChanges; different -> EligibleActivitiesChanged; gaps -> Unverified. A new snapshot alone never implies new score. No-change Git can proceed with genuine new Peer input.

## Guided FE
Project Detail → Đóng góp keeps three cards/current Published result, plus adjustment choices. Combined panel shows six steps, two repository job receipts, errors, exact technical IDs, explicit confirmations and readiness. One bounded polling window per attempt, foreground/focus refresh, manual status check after timeout; no automatic mutation. Session journal keys account+project and preserves exact start RequestId; ambiguous capture uses attempt link. Member retains only own current Published value and own Peer ratings; Creator preview compares current Published and Draft per member, with existing exclusions/hash/history detail. Native layout uses existing Hub components; browser390px checked, native device execution not claimed.

## Schema/deployment
New additive migration **20261123071955_AddGitContributionAdjustments**, after publication-version migration. Creates only two plan tables, FK/unique/check/indexes and audit/correlation triggers; no historical backfill, no scoring data updates. Destructive Down refuses if plan history exists. [Scoped idempotent SQL](../migrations/GitContributionAdjustments.review.sql), SHA256 `E24E505D39EBCB1D02FEA591676E45F86BEF80CE413A18429493B01E798E6AA7`. Existing migrations not rewritten. Fresh chain, populated upgrade, model consistency and SQL reapply exercised on disposable, never E2E.

Separate approval required for migration and binary/runtime rollout. Back up current FE clone and restore-rehearse exact reviewed SQL first. Never blanket migrate a database with unrelated pending history. New schema precedes enabling new API execution. Missing schema fails closed; old worker-off API is not a test of new endpoints.

Normal deployment: existing global worker can handle Manual jobs when intentionally configured. Isolated E2E source configuration (NOT applied here):
```
ASPNETCORE_ENVIRONMENT=E2E
DOTNET_ENVIRONMENT=E2E
UNINET_SKIP_LOCAL_ENV_FILE=true
BackgroundWorkers__Enabled=true
GitHubE2E__DisableUnrelatedWorkers=true
GitHubSync__Enabled=false
GitHubSync__ManualEnqueueEnabled=false
GitHubSync__ScopedManual__Enabled=false
GitHubSync__AdjustmentExecutionEnabled=true
```
Use existing protected E2E launcher/FE clone connection; never root .env. Master registration permits only the adjustment coordinator under E2E isolation, not global GitSyncWorker or unrelated workers. Startup rejects simultaneous legacy pinned mode. Coordinator only selects persisted plan jobs and invokes existing worker with exact Project/Association/Job scope, sequentially, 15min plan deadline and existing bounded retries. Idle scheduler polls plan metadata; every execution is deadline-bound. No scheduled enqueue, webhook, activation or reconciliation. Multiple hosts still use existing lease fences; deployments should run one coordinator for sequential project execution. API action does not alter config. A lease in flight across shutdown expires/reclaims; do not force complete or edit checkpoints. Deadline/failed plans require explicit readback and abandon only after lease safety permits; fresh attempt gets new jobs while old pages/evidence remain audit.

## Exact future manual procedure (requires approval)
1. Backup/restore rehearsal; approve/apply exact new migration on FE clone, verify105→106 and history/fingerprints.
2. Drain verified old API, deploy new binary with isolated flags above, health/target/no competing writer checks. Do not enable global worker.
3. Creator opens adjustment → combined; inspect exact two Project A associations then explicitly confirm Đồng bộ GitHub once. Record plan RequestId/Id + each actual JobId.
4. Wait for each durable Completed/receipt, bounded retry/deadline. If failed, fix source authorization then explicitly recover/end attempt; never force status.
5. Explicitly chốt Git. Reconcile exact plan if response lost; review policy2/readiness/coverage/eligible-change result. Gaps block Peer.
6. Explicitly open new Peer. A/B/C authenticate themselves and submit six actual directed1–5 ratings. No copying/impersonation.
7. Creator closes only ReadyToClose, then returns combined and explicitly creates Draft using owned new Git/Peer IDs. Existing current result remains visible.
8. Review all members/external/exclusions/limitations and comparison. Explicit atomic Duyệt và công bố only after separate authority to publish. Verify new release and old hashes.
9. Restore worker-off configuration after authorized test; no deletion of audit/history.

## Tests
Disposable container `uninet-scoped-activation-release`, ID `e5702f6d5c18c2cabb0dcf98eef8cc607bd3e7e87e9b322912af6d2dd289ab27`, postgres17, host127.0.0.1 port55448 rolepostgres SSLDisable, explicitly named disposable databases. Verified identity before writes. Official fixture uses full migrations and fake local facts. Actual coordinator/pinned worker tests use fake provider with real PostgreSQL persistence; never real GitHub E2E.

- Backend related regression: **55 PASS /0 FAIL /2 SKIP**, including seven combined migration/correlation/rollback/Peer/Run/publication/recovery tests, coordinator/pinned execution, runtime registration and prior Official/scoped tests. Repeated focused reruns not added again. Two SKIP require separate legacy Git MVP fixture env; not new test skips.
- FE focused:36 Contribution +67 related GitHub = **103 PASS /0 FAIL /0 SKIP**.
- Browser:24 existing +8 combined = **32 PASS /0 FAIL**; every API intercepted, no live mutation. New mobile screenshot uses390px.
- Backend build PASS; EF pending-model changes none; feature ESLint clean. Full FE TypeScript remains13 pre-existing errors in opportunities/AssessorProfilePosts, none in changed feature.
- Both git diff checks PASS (existing CRLF warnings only).

## Preservation and limitations
FE clone read-only verified: container `uninet-step2-concurrency`, host127.0.0.1 port55433, database `uninet_github_fe_e2e_20261121`, roleuninet_test,105 migrations; no active jobs. Full-table sorted row-content fingerprints compared across Git jobs/checkpoints/evidence/observations/snapshots/entries/runs/releases/Peer/identities/membership. This covers Project B as well as A; counts/hashes unchanged during final verification. No Auth login or live writes needed in this implementation. A release1/current Run remains unchanged; no new cohort/rating/run/publication. API PID15640 health200 unchanged. Existing dirty work preserved; no commit/push.

Policy unchanged: V2 derives timeline from valid CommitterDate range at cutoff, not CompletedAt; post-completion activity can affect it. Product review recommended before live recalculation, not silently modified. Partial overlap/force-push/source limitations still surfaced by existing readiness. No native device/live GitHub/real new evaluation round is claimed. If Peer is already open and a new blocker appears, finish/resolve its existing lifecycle; no destructive replacement of that cohort. Large historical snapshots incur diagnostic/read validation cost; no speculative optimization or extra retrieval was introduced.

## Files
Backend: new domain/config/contracts/service/coordinator/migration+Designer/SQL/tests; DbContext model/config call; GitSync options/job transaction composition; snapshot capture transaction composition; Official service/controller/registration; diagnostic transaction reuse; runtime registration/tests. FE: new CombinedAdjustment component/state helper/browser test; panel/hook/service/types/reason labels/unit tests; existing browser labels adapted. [FE spec](../../../UniNet_FE/docs/specs/project-contribution-v2.md) documents the integrated path.

**READY FOR SCOPED MIGRATION/ROLLOUT REVIEW — no live adjustment executed.**
