# GitHub integration — Phase 3

Phase 3 adds durable synchronization jobs, checkpointed Git activity ingestion, project-scoped activity visibility, webhook-triggered sync requests, and project activity/status APIs. It does not calculate Contribution scores or change finalized Contribution runs.

## Enabling synchronization

`GitHubSync:Enabled` defaults to `false`. When enabled, the hosted `GitSyncWorker` claims durable jobs using PostgreSQL row locks, renews leases while processing, retries transient failures with bounded backoff, and enqueues due scheduled syncs. Do not enable the worker until all GitHub integration migrations have been applied to the target database. The worker does not apply migrations.

| Setting | Default | Purpose |
|---|---:|---|
| `GitHubSync:Enabled` | `false` | Enables manual and scheduled synchronization and starts the hosted worker. |
| `GitHubSync:DefaultIntervalHours` | `6` | Default schedule interval for project-repository links. |
| `GitHubSync:WorkerConcurrency` | `2` | Number of concurrent worker loops; bounded by the worker to 1–16. |
| `GitHubSync:BatchSize` | `100` | Provider page size. |
| `GitHubSync:RetryBaseDelaySeconds` | `30` | Base retry delay before exponential backoff and jitter. |
| `GitHubSync:MaxRetryAttempts` | `8` | Maximum attempts before a job becomes terminally failed. |
| `GitHubSync:JobLeaseDurationSeconds` | `300` | Lease duration; periodically renewed during processing. |
| `GitHubSync:PollSeconds` | `10` | Idle worker polling interval. |
| `GitHubWebhook:Secret` | unset | HMAC secret configured on the manually registered GitHub webhook. Keep it in a deployment secret store. |
| `GitHubWebhook:MaximumPayloadBytes` | `1048576` | Maximum accepted webhook payload; the API endpoint also enforces a 10 MiB hard ceiling. |

The worker requires PostgreSQL for durable job claiming. SQLite is used by unit tests for model and service behavior, not to verify PostgreSQL lock semantics. A deployment with `GitHubSync:Enabled=false` does not process queued jobs.

## API

All project routes require an authenticated UniNet Student account. Project-level authorization is enforced by application services; activity results are restricted to repository associations and activity visibility grants observed for that project.

| Method and route | Purpose |
|---|---|
| `POST /api/projects/{projectId}/git/repositories/{linkId}/sync` | Queues a manual sync and returns `202 Accepted`. |
| `PATCH /api/projects/{projectId}/git/repositories/{linkId}/sync-settings` | Updates per-link sync settings. |
| `GET /api/projects/{projectId}/git/sync-status` | Returns project-scoped sync status. |
| `GET /api/projects/{projectId}/git/repositories/{linkId}/sync-jobs?page=1&pageSize=20` | Returns paged sync-job history for one linked repository. |
| `GET /api/projects/{projectId}/git/activities?page=1&pageSize=50` | Returns paged, project-visible activity; optional filters include activity type and time range. Raw provider payloads are not returned. |
| `POST /api/webhooks/github` | Accepts GitHub webhook deliveries after validating delivery headers, payload size, and `X-Hub-Signature-256`; supported events enqueue durable synchronization jobs. |

The webhook endpoint does not register webhooks with GitHub. For each test or production repository, an administrator can add a webhook in **Repository Settings → Webhooks → Add webhook**:

1. Set Payload URL to `https://<api-host>/api/webhooks/github`.
2. Set Content type to `application/json` and Secret to the exact value configured as `GitHubWebhook:Secret` for that API deployment.
3. Keep SSL verification enabled and select **Let me select individual events**.
4. Select Pushes, Pull requests, Issues, Issue comments, Pull request reviews, Pull request review comments, and Releases.
5. Save the webhook and use GitHub's **Recent Deliveries** test delivery to verify the API response.

Registration requires repository administration in GitHub; the UniNet OAuth App itself needs no write/admin scope for synchronization. The handler records delivery IDs for deduplication and uses the repository ID in the payload to find active project links. If a link already has an active sync job (pending, running, retry-scheduled, or paused), a new delivery is recorded and coalesced with that job rather than creating a duplicate. The webhook payload is a trigger only; the worker reads activity from the GitHub API using the project's currently linked credential. Scheduled Sync remains the fallback if webhook delivery is unavailable.

## Data and authorization behavior

Provider objects and normalized activities are deduplicated by stable GitHub repository/object identifiers. Project visibility grants are stored separately and only created after a project's own active repository link is synchronized successfully. Unlinking or losing access does not delete provider history; the activity service applies project-link and visibility boundaries when responding.

The additive migrations for Phase 3 are:

- `AddGitSyncInfrastructure`
- `AddGitActivityVisibility`
- `AddGitCommitBranchReferences`
- `AddGitHubWebhookDeliveries`
- `AddProjectGitActivityVisibility`

These migrations must be reviewed and applied through the normal deployment process. No migration should be applied to a shared or production database from development tooling.
