# UNINET — AUTOMATED E2E PEER EVALUATION & CONTRIBUTION V2

Date: 2026-11-20. **READY FOR PHASE 3 — experimental V2 Draft only; not reviewed, finalized or published.**

The user explicitly confirmed ownership/control of both E2E accounts and authorized automated test ratings **A→B=4/5, B→A=5/5, comment=null**. These are user-specified E2E inputs, **not independent production-user evaluations**. Earlier handoff instructions requiring manual member submission are superseded only for this authorized E2E operation. No score was chosen or changed by the agent.

## 1. Preflight and backup

- Verified `127.0.0.1:55433/uninet_github_app_e2e`, role `uninet_test`, container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`. No Neon/production target.
- Project `68f98a9f-d73d-4dcf-be5c-7099b29329c0` remains Completed, Creator A/member B and identity/membership history unchanged.
- Cohort `5f798512-6bce-4c66-b58d-c3d5b859fae7`: **Open, frozen A+B, 0 submissions / 0 revisions / no Peer Snapshot** before writes. API and read-only SQL checked that no evaluation existed; no overwrite was attempted.
- Git snapshot `d56f2e2c-5b40-4508-8746-943ee15ae256`: finalized **policy2**, 693 memberships/102 versions/470 coverage receipts. Official authorized reader/current-source diagnostic reconfirmed **Git Ready, A92.50%/B7.50%**, same frozen membership/input hashes as the prior report.
- Full **103 application-table fingerprints**, schema catalog and all **86 migration IDs** matched the previous final backup. Seven unrelated migrations remain pending. No active Git job, no global/manual/scoped worker, same API PID20740/binary/listener. No runtime restart was needed.
- Existing protected consistent backup was current: SHA-256 `ec950014aacf5e64b6bae01f54b10056d73ec86f31ba4d0be19470460d4d7ff9`; prior actual restore matched all103 tables/86 history IDs. Its checksum, protected ACL and restore evidence were reverified. No baseline drift required overwriting/restoring E2E or taking an equivalent duplicate pre-write backup.

## 2. Independent authenticated submissions

Both accounts separately used official `POST /api/auth/login` and `GET /api/auth/me`; email, account ID and profile ID were matched before using each session. A's bearer token was used only for A actions; B's distinct token only for B actions. Student B used the newer protected `UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi`; no credential reset. Credentials/JWTs remained in memory and were not logged.

| Evaluator → evaluatee | User-authorized score | Comment | Submission ID | Revision/history |
|---|---:|---|---|---|
| A → B | **4/5** | null | `1f0076f6-41c3-4f4b-9862-548e523167bb` | revision1, one matching history row |
| B → A | **5/5** | null | `3e7c29ba-a69f-4b9c-8217-96f93413e88f` | revision1, one matching history row |

Each official evaluation PUT used `expectedRevision:null`, checked first-submission state immediately beforehand and executed **once**, protected by a durable one-attempt marker. No blind retry, rating edit, duplicate pair, self-review or new cohort. Actual response ratings and persisted rows/history matched the exact authorized directions/scores/comments.

Cohort lifecycle: **Open → Collecting → ReadyToClose**. Creator GET plus SQL verified **2/2**, frozen roster, two revision1 rows and corresponding audit history before closure. Member B's response contained only B's outgoing evaluation and no revision history; no incoming A→B rating was exposed through B's session.

## 3. Closed Peer Snapshot and normalization

One authenticated **Creator A POST** to the official cohort `/close` endpoint succeeded. GET reconfirmed Closed and the persisted SnapshotId:

- **PeerSnapshotId: `c6fe092b-ea1f-4e19-b04e-384a7663424b`**.
- Cohort remains the same record and roster; now **Closed**, two submissions/two revisions.
- Policy `ExperimentalPeerV1`.
- Input SHA-256: **`53fb58650017273c7bf96e9193a4a36eb3c06f17b8f5d0da65179a1bec0dd38c`**.

Read-only audit of the actual persisted frozen Peer Snapshot recomputed its canonical hash, checked project/cohort/roster/ratings/revision1/comments, and independently recomputed the existing peer conversion `(received mean−1)/4×100`:

| Member | Received rating | PeerScore | Unrounded PeerShare |
|---|---:|---:|---:|
| A | 5 | **100.00** | **100/175 = 57.142857142857…%** |
| B | 4 | **75.00** | **75/175 = 42.857142857142…%** |

Peer mass=175>0; **Ready**, not InsufficientData. No missing rating was converted to zero and no top-level NotApplicable transfer was used. Snapshot immutability is supplied by existing DB guards and covered by real disposable regression; no destructive immutability probe was performed on live E2E.

## 4. Experimental Contribution Run #4 — Draft / Ready

After peer hash/score validation and Git readiness, Creator A called the existing run API **once** with:

- Git Snapshot `d56f2e2c-5b40-4508-8746-943ee15ae256`.
- Peer Snapshot `c6fe092b-ea1f-4e19-b04e-384a7663424b`.
- Explicit experimental acknowledgment, A+B participant IDs.
- `ExperimentalGitScoringV2`; normalization `GitPeerSeparate60_40V1`.

**RunId: `ad2cf570-9592-465a-bdb0-c80c5112cdef`**, RunNumber **4**, state **Draft**, CalculationStatus **Ready**. No review history, reviewed/finalized timestamps or publication. No direct SQL business write was used.

| Member | Commit | PR | Review | Issue | Raw GitScore | GitShare | PeerScore | PeerShare | ContributionPercent |
|---|---:|---:|---|---:|---:|---:|---:|---:|---:|
| Student A | 100 | 100 | NotApplicable | 60 | **74.00** | **92.50%** | **100.00** | 57.142857…% | **78.36%** |
| Student B | 0 | 0 | NotApplicable | 40 | **6.00** | **7.50%** | **75.00** | 42.857142…% | **21.64%** |
| External | — | — | — | — | — | **0.00%** | — | — | **0.00%** |

Independent arithmetic from frozen run weights:

- A: `.60×92.50 + .40×(100/175×100) = 78.357142857…`.
- B: `.60×7.50 + .40×(75/175×100) = 21.642857142…`.
- Deterministic largest remainder: floor to basis points gives7835+2164=9999; A has the larger remainder and receives the remaining basis point. **A78.36 + B21.64 + External0 = 100.00%**.

Do not confuse raw result `totalScore`/`rawTotalScore` **A84.40/B33.60** with normalized `contributionPercent`. They are different fields and are not normalized by their raw total. No External mass was redistributed.

### Hash and persisted-input verification

- Stored **InputHash** and **CalculationContextHash**: **`291d97e4d33438f05fb651bcb09dda8d571feb93b77d3ff8d9db15b5e0ae465b`**.
- The production `ComputeScoringInputHash` was rerun read-only against persisted InputSnapshotJson and matched. Frozen normalization inputs have Git weights **A0.74/B0.06**, PeerScores **A100/B75**, External0 and both states Ready. The weights are fractional internal mass, not the displayed 74/6 score scale.
- Derived canonical result-audit SHA-256: **`8e0f12fecb0af3bb17a4bb6ae03144cf802e7183523553e33e5d998f99af732b`**. Definition: canonical JSON of RunId, scoring/normalization policy versions, stored InputHash, decoded ResultsJson, NormalizedResultsJson and CategoryStatusJson. **There is no persisted ResultHash column**; this hash is an audit artifact, not a claim of new schema.
- Creator authenticated GET returned the same RunId/InputHash/results. B's Draft detail read **403**; unauthenticated read **401**. No raw private-evidence endpoint was added.
- The first independent local verifier incorrectly expected internal Git weights74/6 instead of0.74/0.06. Its assertion was corrected to the actual contract; response/formula/data were already correct. No production code or run data was changed, no new Run POST was repeated.

Calculated inputs/results are frozen service output, retained with hashes. The current PostgreSQL run immutability trigger protects **finalized** runs; this Run is intentionally still Draft and the normal review lifecycle may change review/status metadata in Phase3. Do not claim Draft is DB-locked against every owner-issued UPDATE. No such mutation was attempted. Old finalized history is unchanged.

## 5. Data preservation and runtime

Exactly seven permitted tables changed:

1. Accounts and RefreshTokens: official independent Auth sessions.
2. ExperimentalPeerCohorts: existing record progressed to Closed.
3. ExperimentalPeerSubmissions: two new rows.
4. ExperimentalPeerRevisions: two matching revision1 audit rows.
5. ExperimentalPeerSnapshots: one immutable snapshot.
6. GitContributionRuns: one new Draft Run #4.

All other application-table fingerprints are identical. A query excluding the single new Run confirms **all three old Runs exactly unchanged**. Legacy Contribution Cycle/participants/evaluations/results, all five Git snapshots and memberships, 470 coverage receipts, evidence/observations, jobs/checkpoints, repository authorization, identities/ownership history and memberships remain unchanged. Same schema catalog and exact86 history IDs; seven old migrations remain pending.

Durable totals:

| Object | Final value |
|---|---:|
| Git jobs / active jobs | **24 / 0** |
| Checkpoints | **470** |
| Evidence versions / observations | **102 / 693** |
| Git snapshots / coverage receipts | **5 / 470** |
| Runs | **4**, new #4 Draft only |
| Cohorts / status | **1 / Closed** |
| Peer submissions / revisions / snapshots | **2 / 2 / 1** |

Backend stayed **PID20740**, same verified binary/start/listener, worker-off: Global=false, ManualEnqueue=false, Scoped=false, JobId=null; unrelated workers disabled. Health **200**; broad readiness **503**, expected and unmodified. No runtime restart, sync, consumer activation, Git capture, migration, reset, review/finalize/publication, watchdog/launcher modification, commit or push.

## 6. Regression and backup evidence

Current-source isolated build: **PASS**, API/Application/Infrastructure built through the test project. EF model consistency: **PASS** (93 source migrations,86 applied, seven intentionally pending); `git diff --check`: **PASS**. Existing nullability/test-analyzer warnings remain non-failing.

Focused relevant regression: **49 PASS / 0 FAIL / 0 SKIP**, counted once. Includes normalization/largest remainder/External/zero peer mass, Contribution v1 calculation/service, experimental lifecycle/idempotency/frozen input/roster guards and the real PostgreSQL HTTP lifecycle case exercising peer authorization/closure/privacy/immutability and Git Run compatibility. Of49 cases, one is a comprehensive PostgreSQL HTTP integration case;48 are other focused cases. This does not recount the prior247 regression cases.

Verified disposable: `uninet-phase12-final-20261120-005800`, ID `4ce237c388b9aaef5832708a94bbe91ebe68358a2737370d809c75a6c7a01538`; restart changed actual mapped port to **55467**, reverified before tests/restores. User postgres, explicit disposable DBs; no destructive test on55433. This task did not repeat a run-creation POST on E2E to test idempotency; existing disposable lifecycle tests cover it.

Protected receipts/backup directory:
`C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/peer-v2-e2e-20261120-012811/`.

Final consistent PostgreSQL backup `final-backup/e2e-peer-v2.dump`:

- **892511 bytes**, SHA-256 **`267e18517b0e811e28e99b7955cf25b5fdb84f6e9a0d1e775185256a5e8f2032`**.
- Exported read-only MVCC snapshot, protected ACL, transport checksum/TOC valid.
- Actually restored to `uninet_peer_v2_final_restore_disposable`; exact **103 application-table fingerprints and86 migration IDs match**. Final current E2E fingerprints also match this backup read point. No restore over live E2E.
- Request-attempt markers, nonsecret responses, authenticated account/profile verification, submission IDs/history, peer/run canonical audits, before/after fingerprints/catalog/history, TRX and runtime/health evidence are retained locally. No JWT, password or App credential was logged. User-authorized E2E score values appear in this report; this is not a production evaluation publication.

No production code, DTO, migration, policy or test implementation needed modification in this task. Repository changes are documentation/handoff only; all existing uncommitted work was preserved.

## 7. Phase 3 handoff

**READY FOR PHASE 3** for this authorized experimental E2E fixture: Git/Peer inputs Ready, immutable Peer Snapshot and auditable V2 Draft created, independently verified60/40 percentages total100.00%, security/history checks and focused regression pass.

Remaining boundaries:

- **Do not submit/edit the ratings, close again, create another Run, repeat Git sync or capture.** The actual IDs above are the continuation inputs.
- No review, finalize, publish or Frontend integration was performed. The unchanged alternate-roster official-finalization guard still protects the legacy A-only Cycle. Phase3 needs an explicit safe review/publication policy for A+B; do not alter the legacy cycle or bypass the guard.
- Existing general partial-patch allocation and historical/post-merge force-push limitations remain documented. They were not exercised by an unresolved overlap in these eligible frozen inputs and were not hidden to mark this experimental run Ready. This E2E result does not establish a universal official scoring algorithm.
- These are explicitly **automated, user-selected E2E ratings**; do not present them as independent real-user production feedback.

Stop before Phase3 actions. No additional test/acceptance phase is introduced.
