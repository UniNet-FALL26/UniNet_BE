# GitHub App operational recovery — Step 5C

## Acceptance follow-up and audit retention

2026-11-12 bounded closeout: Step 5C is COMPLETE at backend implementation/test level. Two shared-repository projects were recovered and processed to completion by the hosted worker with separate persisted checkpoints and activity visibility; lease-expiry replay and transactional delivery enqueue rollback passed on disposable PostgreSQL. The earlier PARTIAL note below is historical. Actual multi-host process termination, monitoring exporter/alert configuration, and live GitHub App E2E remain deployment verification.

Recovery of a terminal App Initial reuses the job and checkpoints. A structured recovery log records the actor, job, recovery time, prior retry count and sanitized previous error code before those error fields are reset. Preserve these logs under the deployment's retention policy: there is no separate persistent Initial-recovery audit record. Recovery does not erase Git Activity history.

Disposable PostgreSQL tests cover page checkpoint replay, lease fencing, transaction rollback at delivery/job enqueue, concurrent requeue and claim, and a shared-repository two-project recovery/isolation case. These tests simulate interruptions at selected boundaries; they do not establish every hard-crash or multi-project worker-replay scenario. Step 5C remains PARTIAL. Live GitHub App E2E and monitoring exporter verification require separately configured test infrastructure.

This guide describes backend behavior under test, not a production readiness declaration. Use the approved deployment and incident procedures. Never put tokens, webhook bodies, signing keys or secrets in tickets or logs.

## Stored delivery recovery

A verified App delivery is staged encrypted. The consumer claims it with a lease, retries transient errors with backoff, and moves it to `Failed` after a permanent error or exhausted attempts. `PendingHandler`, `AwaitingScope`, `RetryScheduled` and expired leases are handled by the worker; do not manually change their database rows.

The `UniNet.GitHubApp` .NET Meter exposes low-cardinality delivery counters and App job enqueue, claim, retry, terminal-failure and Initial-recovery counters; completion includes both Git credential sources. `UniNet.GitHubHealth` records per-process queue/instance state transitions using only component/status labels. These counters require an operator-configured metrics listener/exporter; no external collector is installed by this change. Safe structured logs cover verified deliveries, consumer claims/outcomes, requeues, job claim/completion, reconciliation backoff and access changes. Handler completion is not itself a claim that every delivery was processed successfully. Health transitions are per process and cannot substitute for monitoring shared durable queue state.

An active UniNet Admin may call `POST /api/admin/github/app/deliveries/{delivery-guid}/requeue` with JSON `{"reason":"<incident reference>"}` and an Admin bearer token. Only `Failed` deliveries with a supported route are eligible; processing, processed and unsupported deliveries are rejected. The operation locks the account and existing delivery row, records the actor, reason, previous error and time in `GitHubAppWebhookRequeueAudits`, and atomically resets the attempt counter for the existing delivery. At most three operator requeues per delivery are allowed. Existing payload, identity and history are retained. Requeued deliveries go through the regular scope checks and lease processing. Investigate and fix the underlying cause first; never requeue repeatedly to bypass permission, HMAC or data protection checks. The audit migration refuses downgrade while audit rows exist; preserve and archive them through an approved procedure.

## GitHub redelivery is different

If GitHub never successfully delivered a webhook to UniNet, there may be no stored delivery to requeue. Check the GitHub App's Recent deliveries and request redelivery through GitHub, or use the App webhook delivery API with properly protected App credentials. GitHub does not automatically redeliver failures. GitHub's API uses its numeric delivery identifier for the API request; UniNet deduplication uses the delivery GUID. Do not confuse them. This project does not implement automatic GitHub redelivery. See [GitHub redelivery documentation](https://docs.github.com/en/webhooks/testing-and-troubleshooting-webhooks/redelivering-webhooks) and [GitHub App webhook API](https://docs.github.com/en/rest/apps/webhooks).

## Git sync jobs

The existing worker retries eligible transient failures up to the configured limit and recovers expired leases. Inspect job status, checkpoint, safe error code, next retry and repository authorization before acting. For an App-backed terminal Failed Initial job, an authorized Creator/Git Manager may call `POST /api/projects/{projectId}/git/repositories/{linkId}/sync-jobs/{jobId}/recover-initial` after correcting the cause. The service verifies active installation/grant and live repository read access, rechecks the mapping transactionally, and changes the same Initial job back to Pending; checkpoints and activities remain attached. A repeated request for an already queued/running/retrying job is idempotent. If another job is active, wait for it to finish. After confirmed AccessLost, allow reconciliation to restore access before requesting recovery. A historical failed Initial without any completed Initial suppresses automatic App scheduled sync until the Initial is recovered; OAuth behavior is unchanged. Do not change job rows, checkpoints or credential source manually.

Recovery tests cover a failed Initial with partial checkpoint, expired lease before and after persisted activity, concurrent requests and local revoked/suspended/unknown authorization. Crash injection has not yet covered every delivery/job transaction boundary. The operation does not guarantee recovery if previously authorized activities are no longer visible under current project access rules.

PostgreSQL workers must own an unexpired lease to start processing, commit a fetched page, mark a job complete or mark it failed. Page persistence locks and rechecks the job lease inside its transaction. After a crash, allow the existing lease to expire; a new owner resumes the same job/checkpoints. A recovered terminal Initial is restricted to App-backed jobs and does not switch credential sources.

## Incident procedures

| Symptom | Inspect and recover | Avoid |
| --- | --- | --- |
| Webhook absent | Check GitHub App Recent deliveries, endpoint reachability, subscription and `/health/github/readiness`; request GitHub redelivery if not stored. | Do not create repository-specific hooks or assume a stored delivery exists. |
| Invalid HMAC | Compare configured App endpoint/secret in the secret manager and inspect GitHub's delivery status; correct configuration and redeliver from GitHub. | Never log or copy the secret or body; never disable signature checks. |
| Stored delivery Failed | Inspect safe `LastErrorCode`, App delivery logs and audit, restore provider access/key ring, then Admin requeue if eligible. | Do not reset rows or requeue unsupported/processed events. |
| Pending too long | Check `/health/github/queue`, `/health/github/instance`, retry deadline, lease and reconciliation state; restore worker or provider access. | Do not clear a live lease. |
| Heartbeat stale | Check each instance, database access, worker configuration and progress timestamps; restart according to deployment policy and allow leases to expire. | Do not treat another healthy instance's inactivity as cluster failure. |
| Startup validation fails | Validate App ID, URLs, shared ring path and certificate/secret references without printing values. | Do not disable validation to mask missing credentials. |
| Installation suspended/uninstalled | Verify installation status with GitHub; restore the installation where authorized and let reconciliation revalidate. | Do not infer uninstall from an ambiguous 404 or delete activity history. |
| Grant revoked | Check App installation repository selection and project access; restore access and allow reconciliation. | Do not change credential source or manually mark access active. |
| GitHub 403/404/429 | Inspect safe error codes, rate-limit/reset metadata and reconciliation deadlines; retry after the provider deadline. | Do not classify rate limits or ambiguous 404 as confirmed deletion. |
| Payload decryption fails | Check shared Data Protection discriminator, key ring and all historical certificate private keys; restore the correct key material before requeue. | Do not create a replacement empty ring or delete encrypted data. |
| Certificate rollover fails | Restore access to previous PFX/private keys, verify shared mounts and test decrypt across instances before retrying. | Do not remove previous keys until retention and recovery are verified. |
| Restart during Processing | Allow lease expiry and normal claim recovery; check queue and worker health. | Do not forcibly clear a live lease or create a parallel job. |
| Migration fails | Stop rollout, verify target/history, preserve evidence and backup, diagnose the failing migration, rehearse on disposable PostgreSQL. | Do not run destructive migration removal or edit history to force success. |

## Step 6 prerequisites

Deployment owner supplies a GitHub App ID, App Client ID/secret, App private key and shared webhook secret via a secret provider; installs the App on an authorized test account or organization; selects authorized public/private test repositories; configures required read permissions and subscribed events; and supplies stable test HTTPS App callback, shared webhook and existing OAuth callback URLs. Provide persistent shared Data Protection storage and all required PFX certificates, an isolated PostgreSQL E2E database, reviewed migration history/backups and a test HTTPS tunnel or endpoint. Agree on test users, repository permissions, redelivery and recovery exercises. Secrets must not be provided to the agent or written into documentation. Live GitHub E2E is not performed by Step 5C.
