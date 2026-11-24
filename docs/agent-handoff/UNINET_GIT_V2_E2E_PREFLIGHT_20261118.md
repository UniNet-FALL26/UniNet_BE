# Git Contribution V2 local E2E preflight — 2026-11-18

## Decision

Migration source is ready for explicit operator approval. **No E2E migration was applied.** End-to-end execution is not yet ready: the configured API is unavailable, Student B's saved credential does not match its current password hash, and the old evidence needs a new authorized sync/capture. No scoring rule was changed.

## Verified target and backup

- Container: `uninet-step2-concurrency`, running, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`.
- Published target: `127.0.0.1:55433`, database `uninet_github_app_e2e`, user `uninet_test`, PostgreSQL 17.11. These values were checked against Docker inspection, the E2E configuration, and PostgreSQL session identity. EF received the explicitly verified connection with read-only session options; it did not use the default connection.
- All SQL inspection sessions used `default_transaction_read_only=on`. Only filesystem backup/report artifacts were written.
- Backup: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/pre-v2-20261118-231652/uninet_github_app_e2e.dump`, custom-format `pg_dump`, 718,395 bytes.
- SHA-256: `75f414050019371b02b1f514546e1835574e3da0b890c13f297f548a4d7668a9`, identical in container and host copy.
- Validation: `pg_dump`, archive listing, and full `pg_restore --file=/dev/null` decoding all exited 0. `archive-list.txt`, `manifest.json`, and a read-only database fingerprint baseline are stored beside the dump. No credentials were printed or added to the repository.
- **Restore rehearsal was not run.** The archive is fully readable; actual restore into another database was outside the authorized single-target preflight. This is a database dump, not a backup of application keys/configuration or cluster-global roles. A restore requires the existing role/environment or separately provisioned equivalents. Do not run destructive restore/Down without separate authorization.

## Migration review

Pending migrations:

1. [AddExperimentalPeerCohortsAndGitPolicyV2](../../UniNet.Infrastructure/Data/Migrations/20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2.cs).
2. [GuardGitV2FinalNormalizationReadiness](../../UniNet.Infrastructure/Data/Migrations/20261118152617_GuardGitV2FinalNormalizationReadiness.cs).

E2E has 82 applied migrations, ending at `20261114234836_AddGitExperimentalContributionRuns`. No ExperimentalPeer tables or V2 run columns exist yet.

The first migration adds four peer tables, nullable V2 calculation/peer-reference columns, scoped restrictive FKs, submission/revision/cohort uniqueness, V2 context uniqueness, and immutable peer/V2-input guards. It replaces the old run uniqueness index with the corresponding V1-filtered index and expands the policy check. The second adds the V2 finalized-readiness check. No historical inputs, results, legacy participants, ratings, evidence or observations are backfilled.

Existing evidence/snapshot/finalized-run triggers remain present. Index/check replacement takes table locks; apply during a quiet Git Contribution window. Down refuses populated peer/V2 audit history rather than silently removing it. Preserve the backup; prefer forward fixes after new audit data exists.

[Review SQL](../migrations/GitPolicyV2PeerCohorts.review.sql) matches a newly generated current-source idempotent script after line-ending normalization. EF pending-model-changes passed. No migration or production implementation file was edited in this preflight.

Apply the schema before starting the new application binary: EF queries on the new model can reference the added columns even when reading V1 runs. Do not run mixed old/new contribution writers during rollout. Any later migration execution must reverify target/history and refresh the backup if data has changed. No automatic startup migration or sync was invoked.

## Frozen evidence readiness

Project: `68f98a9f-d73d-4dcf-be5c-7099b29329c0`. Run #3 snapshot: `7b8f784b-e69f-423f-9487-0cf48c4b03d9`.

- Snapshot has 599 entries referencing 71 distinct versions: 65 schema V2 and 6 schema V3. By stored evidence status, 28 versions are Complete and 43 Incomplete. Complete under an older contract does not prove V2-scoring readiness.
- All 19 distinct commit versions have CommitterDate but **none has ParentShas**. The 10 PR-commit versions also lack parent facts. Pure-merge versus substantive merge classification cannot be proven from this snapshot.
- PR/commit and patch evidence exists, but older versions include missing PR-commit and patch/allocation reasons. Issue records distinguish PRs, but comment parent relationships are missing; event records include incomplete event coverage/unsupported event kinds. No review observations exist; that alone is not a complete retrieval-coverage receipt.
- Current repository evidence contains only V2/V3 versions; no new V4 evidence is present. New provider/writer code supports parent/tree facts and source-backed issue/PR relationships through immutable evidence. It must be exercised through a new authorized sync, not used to rewrite the old snapshot.
- Both current Git identities have numeric provider IDs, active OAuth connections, and one distinct recorded identity owner per actor in this project. Historical ownership conflict handling stays fail-closed in the existing service. This local check is not a live GitHub token probe or a re-attribution of Run #3.
- The linked association is Active/unpaused. Its App grant is not revoked; installation is Active with VerificationUnknown=false; both generations are 1. All 22 existing sync jobs are Completed. These are locally persisted states, not proof of immediate external GitHub authorization.
- No unverified actor is declared External merely to complete normalization. External classification and frozen attribution must be re-evaluated in a **new** run using the actual new snapshot. Partial overlap/uncertain merge/missing coverage can still keep that run Incomplete after sync; a new sync is necessary, not a promise of Ready.

## Peer and authorization readiness

- Project remains Completed; Student A remains Creator. Student B is an active project member. Creator inclusion uses effective membership rather than requiring a redundant ProjectMember row. Both accounts are Active Students.
- Saved DPAPI credential verification, performed in memory against the current ASP.NET Identity password hash: A matches; B does not. No password was changed. Actual HTTP login for both is **NOT RUN**: configured `http://localhost:5239` refuses connections and has no listener.
- [Peer controller](../../UniNet.API/Controllers/ExperimentalPeerCohortController.cs) requires authenticated Student JWTs. [Peer service](../../UniNet.Application/Services/ExperimentalPeerCohortService.cs) checks project membership, includes Creator, prohibits self-evaluation, enforces 1–5 ratings, uses revision protection and a unique ordered evaluator/evaluatee pair, and restricts close to Creator. Members see their outgoing ratings; incoming ratings/history are not exposed to them. Complete close freezes a hashed snapshot; database guards prevent subsequent edits.
- A+B requires two actual submissions: A→B and B→A. No cohort, rating, peer snapshot, or run was created in this preflight.
- Legacy cycle remains unchanged with only A. A+B can use an independent experimental cohort/Draft V2 run; existing official review/finalize protection rejects its alternate roster. This preflight does not authorize changing that policy or the legacy cycle.

## Verification and preservation

Current-source isolated build through the test project succeeded, including referenced API/Application/Infrastructure projects. Tests: **28 PASS / 0 FAIL / 0 SKIP**, with no repeated cases counted:

- GitPeerNormalizationTests: 5.
- ExperimentalGitScoringV2Tests: 2.
- GitExperimentalContributionServiceTests: 16.
- ContributionPercentAllocatorTests: 5.

These exercise exact 100.00% allocation with/without External, preservation of External mass, independent Git/Peer normalization, NotApplicable transfer, Incomplete/zero-mass suppression, deterministic largest remainder, missing-peer rejection, cohort privacy/leader close, V1 compatibility and alternate-roster protection. Synthetic ratings were confined to SQLite test fixtures, never E2E.

EF model consistency and `git diff --check` passed. Existing compiler/analyzer and line-ending warnings were observed separately. PostgreSQL migration/trigger write tests were **not rerun on E2E**; this is preflight, not authorization to mutate it. Prior disposable verification remains historical evidence, not a new E2E test result.

Runs #1–#3 remain Draft/V1 with the same frozen input/result digests checked during preflight. Run #3 input hash remains `64d0bc18a6cb388f9fba937101b86a71372254dbd8c4cb584f18606b0365b560`. Snapshot count=3, Git run count=3, legacy cycle count=1, legacy calculation run count=0, legacy peer evaluation count=0. No sync, finalization, membership change, backend restart, commit or push occurred. Existing uncommitted work was preserved.

## Recommended E2E order / required decisions

1. Explicit approval to apply **only the two reviewed migrations** to the verified E2E target. Recheck history and backup freshness immediately before execution.
2. Start the matching E2E binary under its isolated configuration when authorized; avoid unintentionally enabling background live sync before its approval. Verify API health and real A/B login. Student B needs a valid current credential or separately authorized account recovery, not an automatic reset.
3. Approve an official new Git sync for the existing association; wait for successful jobs/checkpoints and inspect V4 source-backed evidence. No GitHub activities need to be fabricated.
4. Capture a new immutable Git snapshot and assess remaining coverage/attribution/overlap limitations. Keep all prior snapshots and runs.
5. Open A+B experimental peer cohort; each user personally submits the other member's real rating. Creator closes only when complete. This can proceed independently of Git retrieval after authentication works.
6. Create a new Draft V2 run referencing the new Git snapshot and closed Peer snapshot. Assert Ready before expecting final percentages; otherwise retain explicit Incomplete/InsufficientData and null percentages. Do not finalize.

**Stop here pending migration approval.** No normalization decision needs to be reopened.
