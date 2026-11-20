# UNINET — PHASE 3 OFFICIAL V2 E2E DEPLOYMENT & FULL VERIFICATION

Date: 2026-11-20. **VERIFIED — OFFICIAL V2 BACKEND DEPLOYED.**

Backend only. The approved migration and current Official V2 binary are deployed. The historical E2E project remains **RequiresOptIn**; Run #4 was not adopted, reviewed, finalized, published or recalculated. Full official write lifecycle was exercised on disposable PostgreSQL, not on historical E2E data.

## A. Deployment and backup

Verified target: container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`; `127.0.0.1:55433/uninet_github_app_e2e`, role `uninet_test`. Effective connection came from the isolated E2E configuration; no generic env/Neon/production connection was used.

Applied **only** `20261119190820_AddOfficialContributionV2Workflow`, using [the reviewed SQL](../migrations/OfficialContributionV2.review.sql). Current-source EF script was regenerated to a separate local file and matched byte-for-byte; the reviewed file was not overwritten.

SQL SHA-256:
`3184034999DABFADE3AD7EACF8E389F52658C629DDD6BB125F19A7FA6D14EF05`.

Execution used `psql -X`, `ON_ERROR_STOP=1`, `lock_timeout=5s`, `statement_timeout=60s` and the script's single transaction. Exit0 and explicit **COMMIT** were verified, then durable schema/history/data were checked. No blanket EF update, separate history stamping, unrelated migration or Down was executed.

### Current backup and actual restore

The protected existing MVCC backup was proven to match the current pre-DDL read point: **all103 application-table fingerprints and all86 migration IDs** matched. Therefore no newer business data or Peer submission was discarded by using it.

- Backup: `%LOCALAPPDATA%/UniNetE2EBackups/peer-v2-e2e-20261120-012811/final-backup/e2e-peer-v2.dump`.
- Bytes: **892511**.
- SHA-256: `267E18517B0E811E28E99B7955CF25B5FDB84F6E9A0D1E775185256A5E8F2032`.
- Protected storage outside Git; checksum and custom-format TOC verified.
- **Actual restore repeated during this task**, into `uninet_official_deploy_restore_disposable` on the separate disposable container below. All103 fingerprints and86 history IDs matched again.
- The exact scoped SQL then succeeded as non-superuser `uninet_test` on that restore:86→87, old data unchanged, reapply idempotent.
- No restore was performed over E2E.

### Quiescence and runtime transition

Old **PID20740** was verified by executable, start time, assembly hashes and sole5239 listener; workers were off, active Git jobs0. It exited through a controlled console shutdown. Before DDL, port5239 was empty and target DB had **zero other client sessions**. No watchdog or unknown process was stopped.

After migration, the solution was built from current source with isolated artifacts. Initial new PID37020 exposed an existing isolation omission: OAuth credential cleanup still ran despite `DisableUnrelatedWorkers=true`. Its startup made **no data changes**, verified across all107 post-migration tables. The E2E-only registration guard was extended to OAuth cleanup and Assessor worker. PID37020 was gracefully stopped, the solution rebuilt, and the corrected binary deployed as **PID40756**.

Final binary:
`%TEMP%/uninet-official-deployed-build/bin/UniNet.API/debug/UniNet.API.dll`.

- API SHA-256: `9E702E326035377D9FF5802B7141987337F162AB78BE3D6861853E75C70325B3`.
- Application SHA-256: `DCD1FC7FEF8327BD40CCF80760969BAD52DECBAA358DD339C0D71BEE21474E5C`.
- Started UTC: `2026-11-19T20:11:15.4403782Z`.
- One Backend instance on localhost5239; Environment=E2E, verified content root.
- Global=false, ManualEnqueue=false, Scoped=false, JobId environment variable **absent**, CLI JobId empty.
- DisableUnrelatedWorkers=true, AssessorOnboarding=false, VerificationWorker=false, SkipLocalEnvFile=true.
- Final startup has no OAuth cleanup query; no automatic migration/enqueue/capture/run creation.
- Health **200**. Broad GitHub readiness **503**, intentionally unchanged because sync is disabled and seven old migrations remain pending.

## B. Database verification and preservation

**87 applied /7 unrelated pending**; exactly one added history ID. All pending IDs remain:

```text
20261106081602_ConsolidateVerificationPayments
20261106141923_AddAssessorOnboarding
20261107051900_AddAssessorContractSigning
20261108023501_AddAssessorPasswordTokenIndex
20261108142140_ArchiveAndRemoveLevel1
20261109013212_ArchiveAndRemoveVerificationEvidence
20261114014337_ArchiveAndRemoveExternalProjectFiles
```

New schema:

- `ProjectContributionWorkflows`, `OfficialContributionRuns`, `OfficialContributionAudits`, `OfficialContributionPublications`.
- Catalog inventory verifies19 constraints including the supporting GitRun alternate key;14 indexes on the four new tables;6 functions;7 enabled triggers.
- Composite GitRun/project and official-run/project identity, restrictive audit FKs, state/revision/finalization checks, idempotency and one-finalized/publication-per-project uniqueness match the disposable rehearsal.
- Enabled triggers: `official_project_workflow`, `official_workflow_immutable`, `official_calculation_immutable`, `official_run_integrity`, `official_audit_immutable`, `official_publication_immutable`, `official_publish_integrity`.
- New-project default selector is enabled. The one existing E2E project has **RequiresOptIn**. Zero live official Runs/audits/publications.

All old schema catalog entries remained identical before/after DDL. Comparing restore to live initially flagged five **old** CHECK expression renderings: dump/restore flattened parentheses/casts. Definitions were inspected; this was not an E2E migration change. New official catalog entries match exactly, and no pre-existing E2E schema entry was removed or changed.

Immediately after DDL **all103 old table fingerprints matched**. After authorized A/B logins, exactly `Accounts` and `RefreshTokens` changed. The remaining **101 old tables**, including all business/history tables, still match the preflight fingerprints.

| Live object | Preserved final state |
|---|---:|
| Git jobs / active |24 Completed /0 |
| Checkpoints |470 |
| Evidence versions / observations |102 /693 |
| Git snapshots / coverage receipts |5 /470 |
| Git Contribution Runs |4 |
| Peer cohort |1 Closed |
| Peer submissions / revisions / snapshots |2 /2 /1 |
| Official Runs / audits / publications |0 /0 /0 |

Legacy Cycles/participants/results, identity ownership/history and project memberships are unchanged. Run #1–#4 and snapshot rows are unchanged, not merely their counts.

## C. Actual live HTTP/API checks

**27 actual HTTP checks PASS** on the final deployed Backend. Authentication used separate official Auth sessions, DPAPI credentials and `/api/auth/me` identity verification. Student B used the newer `UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi`; no password reset. JWT/passwords were not saved or printed.

| Check | Actual result |
|---|---|
| A/B login and `/auth/me` |200; correct independent account/profile |
| A/B Project detail, Creator repository detail |200 |
| Official `/workflow`, A/B |200, RequiresOptIn; canManage true/false; no implicit input/run/publication adoption |
| Unauthenticated official workflow |401 |
| Creator official snapshot/memberships/readiness |409 `OFFICIAL_WORKFLOW_NOT_ENABLED` |
| Creator official runs list/detail, Peer cohort/snapshot |409 same policy code; routes registered, no404/500 |
| Official published own results, A/B |409 same historical-mode guard |
| B official snapshot detail |403 |
| Existing Contribution V1 cycle, A |200 |
| Existing experimental Run #4, A/B |200 /403 |
| Existing Closed Peer cohort, A/B |200; B sees own outgoing ratings only, no history/incoming rating leak |
| Health / broad readiness |200 /503 |

Run #4 `ad2cf570-9592-465a-bdb0-c80c5112cdef` remains **Draft / Ready**, no review/finalization. InputHash remains `291d97e4d33438f05fb651bcb09dda8d571feb93b77d3ff8d9db15b5e0ae465b`; A **78.36%**, B **21.64%**.

The existing authorized internal Snapshot Reader was also exercised with a DML-blocking interceptor: policy2 snapshot `d56f2e2c-5b40-4508-8746-943ee15ae256`,693 frozen observations,470 receipts,restricted0. Peer Snapshot `c6fe092b-ea1f-4e19-b04e-384a7663424b` canonical hash and scores were independently recomputed and matched. No cutoff reconstruction, capture or mutable payload was used.

No suitable live nonmember credential was created or reset; outsider/cross-project authorization is covered by disposable tests. No official write API was called on the historical E2E project. Only Auth POSTs were used live; no review/finalize/publish request targeted Run #4.

## D. Full official disposable workflow

Dedicated PostgreSQL17 container:
`uninet-official-deploy-20261120-0308`, ID `e727f7f3bfc96d947859dff7367b2fdf463a81af0015263f96760231329c2558`, actual host **127.0.0.1:49291**, userpostgres for fixtures/uninet_test for restored scoped DDL. Container/port/database/user were verified before reset/write. E2E55433 was never a test reset/migration target.

Fresh explicit disposable databases exercised all94 source migrations and upgrade baselines. New project defaultOfficialV2, Creator-inclusive three-member roster, verified identities, two eligible repository associations, frozen facts/observations/completed retrieval receipts and a second project sharing a provider repository were set up through test infrastructure.

Actual official services performed policy2 capture, cohort open, authenticated-actor all-to-all submissions, close/freeze, Run creation, review/rejection/resubmit/approval, finalize, publish and own-member read. A real Kestrel/JWT/controller/DI/PostgreSQL test performed the HTTP Run lifecycle and privacy reads. No alternative scoring engine or direct-SQL fake lifecycle success was used.

Representative latest service lifecycle:

- Project `178ca12a-a17d-43ed-8b01-5e0da7f89735`.
- Run `2c11412f-b6e2-41bd-8b56-7782848e8d45`.
- Durable header Finalized/revision3; publication exists, API projects **Published**.
- Audit revisions0..4: Created, Review, Approved, Finalized, Published.
- InputHash `b6a6c0d275444f3ed6d7086b8d36ac703d6e470768f49e215d1d54391992f371`.
- ResultHash `d4439416467f8edb77611792f3bdfbb535b329234bc1caeeb2eabf48ee69b79d`.
- Three members' GitScores30/20/50, PeerScores75/75/75, separate normalized final shares **31.33/25.33/43.34=100.00%**, External0. Own-member reads and lack of private rating/comment/frozen-facts disclosure verified.

Negative/concurrency verification includes historical LegacyV1/RequiresOptIn guards; absent repository/identity setup blockers; incomplete evidence refusing official Run creation; missing all-to-all Peer ratings; applicable zero Peer mass; missing/foreign snapshots; Creator-only writes/reads; stale revision; rejection without feedback; finalize-before-approval; publish-before-finalize; concurrent approval/finalization/publication; same-result idempotency; inactive frozen participant rejection; revoked repository access; hash corruption; frozen calculation/header/audit/publication mutation rejection; original V1/experimental compatibility.

Direct SQL Draft→Approved skip was rejected **23514**, with Draft unchanged. Frozen result/audit/publication mutations rejected **55000**. During finalization, a separate transaction attempted local account suspension after in-transaction validation: the held account lock caused genuine **55P03** under bounded lock timeout. Finalization committed first; subsequent committed suspension caused publication authorization to fail, leaving no publication. No claim of instantaneous external GitHub revocation detection.

## E. Formula and readiness verification

No scoring/ownership policy changed. Commit35%,PR30%,Review20%,Issue15%; category empty weight is not redistributed. Git and Peer normalize separately, final60/40; eligible External remains in Git denominator and receives only its Git allocation.

The rerun includes1/2/7-member allocation tests, three-member/two-repository PostgreSQL workflows, External examples, verified-empty Review coverage, Git/Peer NotApplicable transfer, incomplete/applicable-zero mass, eligible empty categories, exact overlap/dedup and deterministic largest remainder. Examples: member Git60/20 plus External20 and Peer75/25 produces final66/22/External12=100; equal three-way rounding gives33.34/33.33/33.33 independent of input order. Incomplete/InsufficientData produces no final allocation. A single-person Peer component is legitimately NotApplicable, not fake zero-rated Peer.

The live78.36/21.64 fixture was preserved as a compatibility check, not used as a universal scoring expectation. General partial-patch allocation/overlap disputes and historical/post-merge force-push remain documented policy limits; unresolved actual cases block readiness, not guessed points.

## F. Tests, fixes and files

**267 unique PASS /0 FAIL /0 SKIP**, including **98 actual PostgreSQL cases**. These were executed again in this task, not copied from the prior265 report. Two full relevant runs passed; focused reruns of strengthened assertions replace their matching outcomes and are **not added** to267.

- New2 PostgreSQL cases: authorization suspension serialized against finalize; missing Peer/stale revision/ineligible frozen participant rejection.
- Existing lifecycle test strengthened with direct SQL transition rejection and concurrent finalize; completion/upgrade tests now directly assert incomplete official creation refusal and LegacyV1 rejection.
- Suites include Official transition/service/HTTP, experimental/scoring/normalization/Peer, evidenceV2–V5/canonicalization/EP06/coverage/snapshots, hosted worker recovery/scoped sync, OAuth/App/ABA/lease/identity and ContributionV1.
- Isolated **solution build PASS,0 errors**; final incremental build1 existing CS9113 warning. Other fresh build/test warnings include existing nullability,xUnit2031 and fixture-only EF1002. No warning was converted into a hidden test failure.
- `HasPendingModelChanges=false`; exact regenerated migration checksum retained; `git diff --check` PASS. Existing line-ending warnings are non-failing.
- Final one-instance PID/binary hashes/worker-off flags/health and safe log pattern checks PASS. No token/private-key/JWT pattern was found in runtime logs.

Files changed **during this deployment task**:

1. [API Program](../../UniNet.API/Program.cs): honor existing E2E DisableUnrelatedWorkers for OAuth cleanup and Assessor hosted registration. Normal environment behavior unchanged.
2. [Official PostgreSQL tests](../../UniNet.Tests/OfficialContributionPostgresTests.cs): two new cases and focused stronger lifecycle/compatibility assertions.
3. This report and [next-agent handoff](UNINET_GITHUB_CONTRIBUTION_NEXT_AGENT.md).

Previously implemented Official source/entities/DTO/controller/migration/snapshot/SQL were reused; no migration source/review checksum or formula changed. All pre-existing dirty/untracked work retained. No Frontend, watchdog/launcher, E2E env file, commit or push changes.

## G. Remaining boundaries and handoff

**VERIFIED — OFFICIAL V2 BACKEND DEPLOYED.** No new technical/deployment blocker remains for this scoped rollout.

- Historical E2E project and Run#4 remain RequiresOptIn/experimental. Historical adoption is a separate explicit policy/action; this task neither enables it nor provides a bypass endpoint.
- Future real official writes require appropriate project/user authorization; this task's write demonstration is disposable only.
- Keep the seven unrelated migrations pending until separately reviewed/approved. Never run blanket EF update on this E2E database.
- Retain official audit/schema if rolling back binaries. Do not run destructive Down/backup overwrite; use an approved forward correction after any official data exists. Mixed old/new project writers must not be reintroduced.
- External GitHub revocation is enforceable when detected/persisted locally, not before local observation.
- No new E2E sync, snapshot, Peer rating/cohort, Run, review, finalization or publication was created. Stop here.

Protected local evidence: `%LOCALAPPDATA%/UniNetE2EBackups/official-v2-deploy-20261120-030227/` contains target/backup manifest, exact regenerated SQL, restore/rehearsal/DDL logs, all-table fingerprints and catalog deltas, full migration/pending IDs, schema inventory, authenticated HTTP summaries, authorized reader/Peer verification, TRX/latest unique outcomes, disposable published Run receipts and old/new runtime identities/hashes. Secrets are excluded from report/receipts. Disposable containers are stopped after verification; data retained.
