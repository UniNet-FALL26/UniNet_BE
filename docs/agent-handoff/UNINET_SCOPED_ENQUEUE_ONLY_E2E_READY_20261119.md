# UNINET — Scoped Manual Git V4 E2E enqueue-only preparation

## Result

**READY FOR EXPLICIT MANUAL SYNC APPROVAL.** Backend now runs the current Scoped Manual implementation in enqueue-only mode. **No Manual Sync POST, job creation, GitHub fetch/sync, consumer activation, migration, new snapshot, cohort/rating, Run or finalization was performed.**

## A. Runtime

- Verified previous PID **28292**, executable `C:/Program Files/dotnet/dotnet.exe`, exact previously recorded assembly command line and process creation time. IPv4/IPv6 listeners on 5239 belonged to that one PID; no other UniNet API instance was found.
- Stopped only that verified process through Ctrl+C after checking its console contained only the API and signal helper. It exited gracefully; its log contains `Application is shutting down`. No force termination, unknown process stop or watchdog/launcher change.
- Built [current API source](../../UniNet.API/UniNet.API.csproj) successfully: **0 errors / 0 warnings** in this incremental build. Output is isolated at `%TEMP%/uninet-scoped-manual-build/bin/UniNet.API/debug/UniNet.API.dll`; the corresponding Application assembly exposes ManualEnqueueEnabled and the scoped contract. No old binary was reused as the replacement.
- New PID **41368** serves `http://localhost:5239`; one distinct listener owner across IPv4/IPv6. Process identity and command line were rechecked after launch.
- Loaded only the verified `.env.github-app.e2e` configuration into a sanitized child environment; generic `.env` loading is disabled. The original env file was not edited.
- Runtime/log/receipt directory: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/scoped-enqueue-preparation-20261119-133644/`. `runtime.json` contains nonsecret flags, binary hashes and identity; `previous-runtime.json` records shutdown. No credentials/JWT/private keys were printed or written to receipts.

### Effective flags and JobId

| Setting | Value |
|---|---|
| Environment | E2E |
| GitHubSync.Enabled | false |
| ManualEnqueueEnabled | true |
| ScopedManual.Enabled | true |
| ScopedManual.ProjectId | 68f98a9f-d73d-4dcf-be5c-7099b29329c0 |
| ScopedManual.AssociationId | 25fe740d-dfbf-4859-8ce1-257247b4b3e7 |
| ScopedManual.JobId | **null** |
| ScopedManual.ExecutionTimeoutSeconds | 900 |
| GitHubE2E.DisableUnrelatedWorkers | true |
| AssessorOnboarding.Enabled | false |
| VerificationWorker.Enabled | false |
| UNINET_SKIP_LOCAL_ENV_FILE | true |

JobId was removed from the child environment, not populated with a placeholder or generated GUID. An explicit empty command-line configuration value `--GitHubSync:ScopedManual:JobId=` prevents any JSON default from supplying a value. The actual startup log contains:

`Scoped manual consumer is idle until an approved JobId is configured.`

That branch occurs only after scoped validation and only with the **bound nullable JobId equal to null**; the consumer returns before creating worker scopes, claim queries or auxiliary loops. ManualEnqueueEnabled is present in the freshly built contract and injected as true; no write-endpoint probe was used to test it.

## B. Database preflight / isolation

- Container: `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`, running with only `127.0.0.1:55433` published for PostgreSQL.
- Explicit target: `127.0.0.1:55433/uninet_github_app_e2e`, user `uninet_test`. Inspection sessions used `default_transaction_read_only=on`; no default/generic/Neon/production connection.
- All **85 migration IDs** matched the deployed baseline. EF read-only list confirms exactly seven old migrations still Pending:
  - 20261106081602_ConsolidateVerificationPayments
  - 20261106141923_AddAssessorOnboarding
  - 20261107051900_AddAssessorContractSigning
  - 20261108023501_AddAssessorPasswordTokenIndex
  - 20261108142140_ArchiveAndRemoveLevel1
  - 20261109013212_ArchiveAndRemoveVerificationEvidence
  - 20261114014337_ArchiveAndRemoveExternalProjectFiles
- No database update, migration application or history modification.
- Project remains Completed/Creator A; association belongs to that project and the expected repository, Active/unpaused/linked. Installation is Active, VerificationUnknown=false; grant not revoked; both authorization generations remain 1.
- Before runtime change, all **96 historical table fingerprints** matched the prior post-auth deployment baseline. No actual business-data drift or extra runtime was found.
- A fresh full baseline captured **all 102 current application tables**, including the six added specialization/peer tables, plus complete migration history separately.
- **After startup, all 102 table fingerprints remained identical.** After the two authorized logins, only Accounts and RefreshTokens changed as expected; the other **100 tables remained identical**. Migration history remained identical throughout.
- Host database connections observed after checks belong to PID 41368; the database client was idle. No second API/global worker was observed. The no-JobId startup branch and unchanged job/installation/webhook/checkpoint fingerprints provide the execution-isolation evidence, not merely listener counts.

### Final observed counts

| Record group | Result |
|---|---:|
| GitSyncJobs | 22, all Completed |
| Pending / Running / RetryScheduled jobs | 0 |
| GitSyncCheckpoints | 424, unchanged |
| ProjectEvidenceObservations | 599, unchanged |
| Evidence versions | 65 schema V2 + 6 schema V3 |
| Evidence schema V4 | 0 |
| Project Evidence Snapshots | 3 |
| Git Contribution Runs | 3 |
| Experimental Peer Cohorts / submissions / snapshots | 0 / 0 / 0 |
| App webhook deliveries | 7 Processed, unchanged |

Run #1–#3 full rows, input/result hashes, immutable snapshot headers/entries, evidence facts, identity/membership histories and the legacy cycle were covered by the unchanged fingerprints. No Scheduled job, new lease, checkpoint advancement, webhook consumption, reconciliation/activation timestamp/generation change, or durable Git worker heartbeat change appeared.

## C. HTTP / authorization

**15 PASS / 0 FAIL** login/read/authorization assertions; health checks recorded separately:

| Check | A | B |
|---|---:|---:|
| Login | 200 | 200 |
| Auth me | 200 | 200 |
| Project detail | 200 | 200 |
| Exact repository association GET | 200 | 200 |
| Sync-status GET | 200 | 200 |
| Existing job-list GET | 200 | 200 |
| Existing Run #3 GET | 200 | **403 expected** |

Anonymous Project GET returned **401 expected**. B used the newer protected credential at `UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi`; no reset or old credential. JWTs/passwords stayed in memory. No peer-rating/private-evidence payloads were logged.

Only POST requests made by this task were the two explicitly authorized `/api/auth/login` calls. No POST to any sync endpoint was sent, including invalid/test requests.

## D. Scoped safety / broad readiness

- `/health`: **200**, status=ok.
- `/health/github/readiness`: **503**, unchanged semantics. Global sync is disabled, and the existing check also requires no Pending migrations. No guard was changed, no history was stamped and no readiness value was forced.
- JobId-null branch is confirmed in the real new process. It starts neither scoped claiming nor normal/global loops; no scheduled polling, webhook consumption, installation reconciliation or pending activation was started.
- No claim/enqueue/sync-POST or outbound GitHub request markers appear in the new runtime log. Together with source control flow and unchanged Git/authorization data, this verifies no worker fetch/ingestion during preparation. No live GitHub probe was needed in this task.
- Existing OAuth security housekeeping remains registered as before; no pending expired credential cleanup exists and its table fingerprints did not change. Assessor task bodies return with Enabled=false; unrelated worker registrations remain disabled. No feature requiring the seven missing migrations was activated for these checks.
- This is **enqueue-only readiness**, not proof that a live manual job has been accepted/executed or that broad deployment readiness is Healthy. The 98-test implementation regression remains historical evidence; it was not rerun or counted as new tests in this runtime-preparation task.

## E. Next approval / pinned-consumer procedure — NOT PERFORMED

Explicit approval is still required for the following real job and subsequent restart/consumer activation:

1. Reverify target, scope, authorization and no other global workers/in-flight jobs. Authenticate Creator A afresh; keep the access token in memory.
2. Send exactly one authenticated request, without inventing a JobId:

   `POST http://localhost:5239/api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/git/repositories/25fe740d-dfbf-4859-8ce1-257247b4b3e7/sync`

3. Record `job.id` and `wasAlreadyQueued` from the actual response. Verify the job's association, project through its association, repository, and `SyncType=Manual` (current enum numeric value **3**). Do not assume the first/latest row is the approved job. Scoped mode rejects a Scheduled job rather than converting/reusing it.
4. Merge the [pinned consumer template](config/git-scoped-manual-consume.env.example) with the isolated E2E base, setting only the **verified response JobId**. **Remove or replace the current empty command-line JobId override**: leaving `--GitHubSync:ScopedManual:JobId=` would override the environment and correctly keep the consumer idle. Never start the template placeholder.
5. Controlled restart of only the verified E2E process is required; IOptions does not dynamically pick up a new JobId. Keep global Enabled=false and unrelated jobs disabled. The consumer claims only this ProjectId/AssociationId/Manual JobId.
6. Monitor the authenticated existing job-list/status GET APIs, selecting the recorded JobId, plus safe execution logs until Completed/Failed/Cancelled/Paused or deadline. Do not report a Pending/RetryScheduled/Running job as completed.
7. On cancellation/deadline, respect existing committed page checkpoints. A job may remain Running until lease expiry or RetryScheduled until NextAttemptAt. Revalidate authorization and recover with the same JobId under the existing lease/retry rules; do not modify status/checkpoints, create another job silently, or force Completed.
8. Restore worker-off configuration after the approved run, with manual enqueue disabled and no pinned consumer. No automatic snapshot, Peer Cohort/rating, V2 Run or finalization is authorized by this preparation.

### Remaining operational limits

- Manual enqueue is now deliberately enabled for the approved scope. Do not click a frontend sync button or invoke another script before approval; a permitted Creator/Manager request could create a Pending job even though the consumer remains idle.
- Other global worker instances must stay off. This scoped process cannot constrain a separate normally configured worker.
- Incoming GitHub webhooks can still be durably acknowledged by the existing endpoint; enqueue-only mode does not consume them. None arrived/changed during this verification window.
- Broad GitHub readiness remains 503; this does not authorize applying the seven migrations or relaxing its guard.
- Preserve the currently loaded artifact directory; use a different isolated output if a later code change requires rebuilding while this process runs.

## Files / retained receipts

No production code, env file, launcher, watchdog or migration source changed. Added this report and a latest handoff note. Local verification artifacts include before/after fingerprints, all migration IDs, state summary, nonsecret runtime/old-runtime records and HTTP statuses under the runtime directory above. Existing uncommitted work remains intact.

**READY FOR EXPLICIT MANUAL SYNC APPROVAL. Stop before POST and pinned consumer.**
