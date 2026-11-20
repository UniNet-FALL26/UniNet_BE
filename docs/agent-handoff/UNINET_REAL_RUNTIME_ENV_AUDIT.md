# UNINET — REAL RUNTIME ENV AUDIT

Date: 2026-11-20. Backend-only configuration work, source HEAD `213e648c039b7bb2d4d9b9437f914a7a771e3763` plus this task's uncommitted changes.

## Status

**Base API: CONFIG_READY_WITH_WORKERS_DISABLED.**

**GitHub OAuth/App/webhooks: MISSING_REQUIRED_SECRETS + MISSING_PUBLIC_ENDPOINTS.** They are not ready for activation merely because the API starts. No credentials were copied from E2E, generated, replaced or requested through chat. Worker ownership for the shared Neon deployment remains unverified; all persistent background work is therefore disabled in the local root environment file.

This is not a migration/deployment receipt. No migration, schema change, business-data write, OAuth authorization, token exchange, webhook registration, sync, snapshot capture, peer evaluation, contribution run or finalization was performed on Neon.

## 1. Discovery and inventory

Reviewed [backend instructions](../../.github/instructions/backend.instructions.md), [Program](../../UniNet.API/Program.cs), [environment loader](../../UniNet.API/Services/LocalEnvConfiguration.cs), runtime options, service registration, hosted services, OAuth/App/webhook controllers and services, launch profiles, E2E launch scripts, root/E2E configuration and JSON configuration. Root `.env.example` did not previously exist.

The 126-entry key-level inventory is [UNINET_REAL_RUNTIME_ENV_INVENTORY.csv](UNINET_REAL_RUNTIME_ENV_INVENTORY.csv). It records key, environment spelling, consumer, purpose, requirement, category, sanitized status, configuration source and recommended action. It contains **no configuration values**. Missing optional settings and code defaults must not be counted as missing startup secrets. Configuration sections are containers, not scalar environment keys.

| Area | Actual consumer / configuration | Current result |
|---|---|---|
| Database | `ConnectionStrings:UniNet`; [Program](../../UniNet.API/Program.cs) | PRESENT; original complete value preserved |
| JWT | `Jwt:Key`, issuer, audience, access minutes; [AuthService](../../UniNet.Application/Services/AuthService.cs) | Signing key PRESENT and >=32 UTF-8 bytes; issuer/audience/lifetime inherited from JSON |
| GitHub OAuth | [GitHubIntegrationOptions](../../UniNet.Application/Services/GitHubIntegrationOptions.cs), [GitHubOAuthService](../../UniNet.Application/Services/GitHubOAuthService.cs) | Credentials, callback, named return target and persistent key ring MISSING |
| GitHub App | [GitHubAppOptions](../../UniNet.Application/Services/GitHubAppOptions.cs), [deployment validator](../../UniNet.API/Services/GitHubDeploymentReadiness.cs) | App identity, credentials, PEM, callback, return target, certificate/key protection MISSING; disabled |
| Webhooks | [OAuth webhook options](../../UniNet.Application/Services/GitHubWebhookOptions.cs), App options | Both secrets MISSING; no registration or delivery processing performed |
| Sync | [GitSyncOptions](../../UniNet.Application/Services/GitSyncOptions.cs) | Global/manual/scoped flags false; tuning retains source defaults |
| Contribution | [existing service registration](../../UniNet.Application/Services/GitExperimentalContributionServiceRegistration.cs) | Snapshot/peer/experimental/official services remain registered; no scoring ENV exists or was added |
| Cloudinary | [CloudinaryImageStorage](../../UniNet.API/Services/CloudinaryImageStorage.cs) | Three existing values PRESENT, unchanged; provider acceptance not tested |
| PayOS | [PayOSPaymentProvider](../../UniNet.Application/Services/PayOSPaymentProvider.cs), [PaymentOptions](../../UniNet.Application/Services/PaymentOptions.cs) | Existing alias credentials and checkout URLs PRESENT, unchanged; no payment request |
| Gemini | Moderation/extraction/portrait services | Existing API key and portrait-model setting PRESENT, unchanged; base model default retained; no external request |
| LiveKit | [LiveKitTokenService](../../UniNet.Application/Services/LiveKitTokenService.cs) | Credentials/URL MISSING; feature unavailable, not a startup blocker |
| Jitsi | [MeetingService](../../UniNet.Application/Services/MeetingService.cs) | JSON base URL retained |
| Google login | `Google:ClientIds` in AuthService | MISSING optional feature configuration |
| Assessor/email | [contract options](../../UniNet.Application/Services/AssessorContractContracts.cs), [SMTP sender/worker](../../UniNet.API/Services/AssessorContractWorker.cs) | Disabled; conditional SMTP/template/storage/key-ring inputs not fabricated |
| Verification | [policy](../../UniNet.Application/Services/ProjectVerificationPolicy.cs), [worker](../../UniNet.API/Services/VerificationBackgroundWorker.cs) | Approved JSON scheduling policy retained; worker explicitly disabled |
| Upload/storage | [upload reader](../../UniNet.API/Services/UploadFileReader.cs), [profile storage](../../UniNet.API/Services/ProfileImageStorage.cs) | Existing limits retained; local profile directory is derived from content root, not an invented ENV key |
| CORS/logging | Program and JSON files | Existing values retained; limitations below |

No active root environment key was found to be a retired Task-specific configuration key. Verification reports, payment, file storage and historical Contribution compatibility are separate active concerns and were not removed.

## 2. Configuration precedence and parser

The runtime custom loader chooses `UniNet.API/.env` if present, otherwise the repository root `.env` when the parent contains the solution. No API-local override file was present. It adds parsed values after the standard JSON/Development user-secrets providers, then environment variables, then command-line arguments. The API assembly's user-secrets store was checked without displaying values: zero configured leaf entries. Relevant process overrides were checked by key/value equality without printing their contents; none displaced the approved database selection in this audit.

- Use `Section__Property`, numeric array indices and named dictionary children.
- Parser accepts separate comment lines, optional `export`, matching outer quotes and one assignment per physical line.
- It does **not** interpolate variables, decode escaped newline sequences or support multiline PEM blocks.
- Arrays merge by index with JSON; a root `Cors__Origins__0` does not remove JSON indices 1 and 2.
- Host environment/content root/URLs are selected by ASP.NET hosting before this custom file load. Supply `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` and host URLs through the process/IDE/platform or use the explicit `--urls` CLI argument. Do not assume putting a host-level setting into this late-loaded file changes `IHostEnvironment`.
- `UNINET_SKIP_LOCAL_ENV_FILE` is checked from the process environment, not from the file it is intended to skip.
- Design-time [DbContextFactory](../../UniNet.Infrastructure/Data/UniNetDbContextFactory.cs) has a different loader and can read the root database key. It also has a legacy localhost fallback. No EF update/EnsureCreated/Migrate command was invoked; do not use an implicit tooling target.

Launch profiles still select Development and port 5239. That port is already used by E2E; the validation host used an ephemeral loopback port instead. No launch profile or E2E launcher/watchdog was edited.

## 3. Local normalization and security

The root file was confirmed Git-ignored and untracked before editing. A byte-for-byte backup was created **outside Git**, in `%LOCALAPPDATA%/UniNetRuntimeEnvAudit/20261120-104917/env.original`, before normalization. Its directory grants access only to the current Windows user and SYSTEM. Protected receipts include the original SHA-256 and sanitized before/after checks. No backup content was printed.

The existing **13 assignments** were regrouped into 14 documented sections. Their original assignment text, parsed values and existing annotations were preserved. The file was not replaced with the example template. Eight explicit nonsecret safety flags were added:

```dotenv
BackgroundWorkers__Enabled=false
GitHubApp__Enabled=false
GitHubApp__ValidateDeployment=false
GitHubSync__Enabled=false
GitHubSync__ManualEnqueueEnabled=false
GitHubSync__ScopedManual__Enabled=false
VerificationWorker__Enabled=false
AssessorOnboarding__Enabled=false
```

No placeholder was inserted for a missing credential in the real file. [The new example](../../.env.example) contains only blank conditional inputs, source defaults and format comments; it includes no actual host, database identity, token, PEM, password or merchant credential. Required startup blanks deliberately cannot start a usable service until the operator provisions approved values. PayOS's existing alias spelling was preserved instead of introducing blank canonical keys that would shadow it.

Recovery is a local configuration-file restore from the protected original after checking that the file has not received subsequent legitimate edits. It is not a database restore, secret rotation or permission to overwrite newer changes.

## 4. Specific source configuration gap and fix

Previously, `GitHubSync:Enabled=false` did not stop Notification, token cleanup, OAuth cleanup or portrait cleanup in a normal Development/Production host. Several loops execute writes immediately; portrait cleanup can also delete remote Cloudinary assets. The only common registration guard was E2E-specific. Pretending a real Neon runtime was E2E solely to disable these workers would misrepresent its environment.

Added [RuntimeWorkerRegistration](../../UniNet.API/Services/RuntimeWorkerRegistration.cs) and connected it from Program:

- `BackgroundWorkers:Enabled=false` omits all persistent DB/external-writing hosted workers, including GitSyncWorker.
- Default is **true**, preserving the previous normal deployment's eight hosted registrations and existing E2E scoped-worker behavior.
- Existing per-feature authorization, lease, generation, retry and readiness logic is unchanged.
- Read-only `AccountSessionConnections` supervision remains registered outside the existing E2E isolation setting. The master flag does not disable request authorization or SignalR session revocation.
- Explicitly enabling App configuration still runs its validation even if workers are disabled; the master flag is not a credential/readiness bypass.
- This is a worker registration switch, **not** a global read-only API mode. Authorized request handlers can still write when invoked. The temporary validation process additionally enforced a read-only database connection and received no authenticated write requests.

An old published binary will ignore this new key. Only use the newly built source/binary for this local profile. Do not assume placing the flag into an old deployment stops its workers.

## 5. Worker activation matrix

| Worker / loop | Registration / control | Default / startup behavior | Persistence and concurrency | Local result |
|---|---|---|---|---|
| GitSyncWorker jobs/polling | New master + existing `GitHubSync:Enabled` or explicit scoped mode | Master defaults true; Git global/manual/scoped default false | SQL claims, row/advisory locks, lease/heartbeat, generation fencing, atomic page/checkpoint, bounded retry retained | NOT REGISTERED; READY_BUT_DISABLED |
| App reconciliation/pending activation | Same Git worker, global mode and App enabled | Can probe/write when enabled | Existing installation coordination and authorization checks | DISABLED |
| App webhook consumer/heartbeat | Same Git worker, global mode and App enabled | Claims deliveries/records heartbeats when enabled | Existing SKIP LOCKED delivery claims, lease/advisory mechanisms | DISABLED |
| NotificationBackgroundWorker | Master and existing E2E guard | Previously registered normally; processes work immediately | Broadcast/outbox/retry/delivery writes; do not assume a single elected process for every operation | NOT REGISTERED |
| ExpiredTokenCleanup | Master and E2E guard | Immediate pass, then daily | Deletes expired/revoked refresh tokens; no separate worker lease | NOT REGISTERED |
| GitHubOAuthFlowCleanupWorker | Master and E2E guard | Immediate pass, then hourly | Clears expired pending credential data; no separate election | NOT REGISTERED |
| VerificationBackgroundWorker | Master, E2E guard, `VerificationWorker:Enabled` | Existing feature default true, explicit local false | Existing per-task PostgreSQL advisory locks; payment/deadline/refund/moderation operations | NOT REGISTERED |
| AssessorContractWorker | Master, E2E guard; enabled feature/options in task services | Existing normal registration; 5-second loop | PDF/email/activation state; external email and local files | NOT REGISTERED |
| PortraitCleanup | Master and E2E guard | 15-minute timer | Asset state writes and possible Cloudinary deletion, existing concurrency checks | NOT REGISTERED |
| AccountSessionConnections | Existing E2E isolation only | Per-instance 5-second supervision of its own sockets | DB reads only; no connections means no DB query; closes invalid sessions | RETAINED in Development |
| Official Contribution | Service registration, no dedicated autonomous hosted worker | Only authorized service/API operations | Existing snapshot/peer/review/finalize/publish guards | REGISTERED, NOT INVOKED |

Before enabling any persistent loop, identify all local/Render instances, confirm ownership, capacity/credentials/schema, understand each loop's concurrency model and enable only the intended process. The existence of leases in Git does not elect a leader for every unrelated worker.

## 6. Database verification — no migration

The original Neon connection was kept unchanged, including `SSL Mode=VerifyFull`. Npgsql pooling remains enabled with maximum pool size 100; no performance tuning or pooler endpoint substitution was made. Size per-instance pools against the actual hosted connection budget before scale-out.

The audit connected to the previously verified Neon project/branch and database/role in an explicit Repeatable Read **READ ONLY** transaction, with bounded timeouts, then rolled it back. Credentials and the full connection string were not exported.

**Actual history differs from the request's baseline:** 104 applied history rows, all 103 source migration IDs present, zero source pending IDs, one additional ID:

`20261119081037_AddMessagingAndPush`

That ID has no corresponding migration in the current assembly. It was not created or stamped by this task. No row was deleted/inserted to hide the difference. `HasPendingModelChanges` is false; that checks source model/snapshot consistency, not compatibility of every live database object. Review the additional migration's original source in a separate reconciliation task before future deployment tooling. No automatic migration is present in normal API startup. The profile-development seed is explicit CLI-only and was not invoked.

## 7. GitHub configuration and missing checklist

### Actual routes

| Purpose | Source-confirmed route |
|---|---|
| OAuth start | Authenticated `POST /api/github/oauth/start` |
| OAuth callback | `GET /api/github/oauth/callback` |
| OAuth completion | Authenticated `POST /api/github/oauth/complete` |
| App authorization start | Authenticated Student project route `/api/projects/{projectId}/git/app/authorization/start` |
| App callback | `GET /api/github/app/authorization/callback` |
| OAuth/manual webhook | `POST /api/webhooks/github` |
| App webhook | `POST /api/webhooks/github/app` |

To operate GitHub features, provision approved values outside chat:

1. OAuth Client ID/Secret, callback and at least one named frontend return target; decide approved mobile redirect schemes if needed.
2. Existing persistent Git credential key-ring location and protection material. Preserve decryption compatibility; do not generate a replacement key ring for an existing deployment.
3. GitHub App ID, Client ID/Secret, PEM content, HTTPS callback, return targets and webhook secret. No global InstallationId ENV exists: actual installation/grant/repository associations are database authorization state.
4. OAuth webhook secret separately, if that integration path is used.
5. Public HTTPS origin/tunnel/host and exact provider-registered callback/webhook URLs. A localhost API health check cannot verify GitHub dashboard settings or public reachability.
6. Verify real App installation state, repository scope and necessary endpoint permissions through the existing authorized services once activation is approved. Current source verifies numeric user identity, installation availability, repository read permission and administrator permission for management. No live permission check was attempted without credentials.

`GitHubApp:PrivateKeyPem` is passed directly to `RSA.ImportFromPem`. It accepts actual PEM **content**, not a file path, Base64 wrapper or literal backslash-n string. Use a secure multiline process environment/secret-provider value. Do not store a physical multiline block in this single-line parser. App deployment validation requires RSA >=2048 bits and a current private-key certificate. Missing actual material cannot be validated offline; unit fixtures are not evidence that real credentials are valid.

OAuth uses named return-target validation and state protection; tokens use the existing Data Protection protector. Scope defaults in current source are `repo` and `offline_access`; they were not changed or claimed to match a particular GitHub dashboard configuration. Signature verification remains HMAC-SHA256 over the original payload with fixed-time comparison. Delivery retry/deduplication and generation checks are unchanged.

### Important configuration interactions

- `PayOS:*` takes precedence over `PAYOS_*` aliases, including an empty canonical value. Do not add blank canonical values over existing aliases.
- `LIVEKIT_*` aliases take precedence over `LiveKit:*`. Use one intentional naming scheme. LiveKit requires a secure origin and >=32-byte secret; no fabricated token was issued.
- Program configures one global Data Protection provider twice. Setting `AssessorOnboarding:KeyRingPath` also sets its application name/storage and can override the earlier GitHub provider configuration. Both paths are currently unset. Coordinate that configuration and existing token decryption before enabling these integrations together; do not independently select incompatible key rings.
- Non-E2E source adds fixed frontend origins in addition to configured CORS. Existing Development JSON origins also merge by index. They were not widened or removed in this task; public hosting needs an explicit effective-origin review.
- No ENV key enables Official V2 scoring, substitutes Peer data, or changes category weights. Existing persisted workflow and completeness/authorization rules are authoritative. No Contribution cycle or immutable input/result was modified.

## 8. Validation results

| Check | Result |
|---|---|
| Root file ignored/untracked | PASS |
| Protected original backup | PASS |
| All 13 original parsed values preserved | PASS |
| New flags bind as valid booleans; Git/manual/scoped/App off | PASS |
| Duplicate assignments / invalid key binding | None in normalized root file |
| Missing startup signing/database config | None; actual JWT size check PASS |
| App credential validation | Intentionally not activated; missing actual credentials, not PASS as enabled App |
| Configuration/host-focused automated tests | 8 PASS / 0 FAIL / 0 SKIP |
| Current-source Backend build | PASS, zero errors; existing warnings retained |
| Model consistency | PASS, no pending model changes |
| Neon query mode | READ ONLY; zero source pending migrations, extra history ID explicitly recorded |
| Temporary real Development startup/DI | PASS; newly built binary, isolated loopback port, persistent workers omitted |
| `/health` | 200 |
| `/health/github/readiness` | 503, expected while App/global sync disabled |
| `/health/github/instance` and `/queue` | 200 **Degraded**, expected existing ASP.NET health mapping when features disabled; not a healthy deployment claim |
| Unauthenticated auth-me / Git connections / Official workflow | 401 / 401 / 401; no account or private evidence queried |
| Temporary validation host | PID 6720 / port 60894, stopped after seven successful HTTP checks |
| Existing E2E | Not restarted, repointed, configured or migrated |

The first HTTP-check harness incorrectly expected HTTP 503 for disabled instance/queue health. Source explicitly returns Degraded, which maps to 200 by default. The harness was corrected to assert **both** 200 and the `Degraded` response body; no health guard or production assertion was weakened.

Fresh full isolated regression: **1,134 PASS / 0 FAIL / 65 SKIP across 1,199 distinct test IDs**, including **157 PostgreSQL PASS**. This includes the eight configuration/host tests; repeated executions were not added to totals. SKIP remains SKIP, not PASS. Both fresh TRX evidence and the distinct-ID summary are retained outside Git.

The initial full execution was 1,133 PASS / 1 FAIL / 65 SKIP. Its sole failure was the `upgrade:true` immutable snapshot fixture: the reused disposable database had already been upgraded by the previous run, while this case explicitly requires the pre-snapshot schema. The upgraded synthetic database was preserved under another disposable name; a schema-88 synthetic baseline was cloned into the expected fixture name. Both parameter cases then passed, without modifying production logic or assertions. Test-managed schema upgrades occurred only in these isolated fixture databases, never Neon/E2E. The regression container's actual published port was reverified as 61266 after restart, not assumed to remain 63525. It has been stopped again with its fixture data retained.

Secret-safety verification confirms all13 original parsed values unchanged,21 unique normalized assignments, root file ignored/untracked, and no original secret values found in changed workspace text or completed local logs. `git diff --check` passed. Existing E2E remains PID40756, health200; no runtime replacement occurred.

## 9. Local and hosted recommendations

For local inspection, build current source, set the host environment externally, use an unoccupied port with `--no-launch-profile`, and keep the eight documented flags false. Do not substitute an E2E environment/file simply to suppress workers. The root file targets the approved real Neon database; authorized write APIs are still real writes, so this profile is not a playground for fixture creation.

For Render/hosted runtime, supply exact configuration through platform environment/secrets, set `UNINET_SKIP_LOCAL_ENV_FILE=true`, and persist the approved key ring/certificate/private files outside the repository and ephemeral image. Validate paths, callbacks, return targets, CORS and credential ownership before enabling App or workers. Do not deploy the local root file or use E2E launch/watchdog scripts. Do not start duplicate workers based on a green liveness endpoint.

## 10. Files and remaining work

- Local ignored `.env`: regrouped, original values retained, explicit safety flags added.
- [`.env.example`](../../.env.example): new source-based reference, no real credentials or private target.
- [Program](../../UniNet.API/Program.cs) and [RuntimeWorkerRegistration](../../UniNet.API/Services/RuntimeWorkerRegistration.cs): minimal persistent-worker registration gate, defaults preserve old behavior.
- [RuntimeWorkerConfigurationTests](../../UniNet.Tests/RuntimeWorkerConfigurationTests.cs): four cases for disable/default/E2E compatibility and retained session protection.
- This report, [inventory](UNINET_REAL_RUNTIME_ENV_INVENTORY.csv) and [NEXT_AGENT](UNINET_GITHUB_CONTRIBUTION_NEXT_AGENT.md).

Missing GitHub/LiveKit/Google/Assessor inputs are operator configuration requirements, not reasons to invent values. The additional Neon history ID requires provenance review separately; this task does not reconcile it. No migrations, business policies, JWT/provider credentials, existing E2E files, watchdog/launcher or finalized data were changed. No commit/push.
