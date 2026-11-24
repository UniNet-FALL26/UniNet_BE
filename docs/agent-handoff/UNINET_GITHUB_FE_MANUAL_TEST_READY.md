# GitHub FE manual test fixtures — READY, with runtime target incident disclosed

> Repository-access update: Backend now returns optional, sanitized `configurationUrl` from the GitHub installation's `html_url`. No new endpoint/migration. Current source passed 48 focused Backend tests; current live installation discovery returns the exact Configure URL and two eligible repositories. FE account-scoped picker now supports Only select repositories, direct Configure navigation and explicit fresh discovery. See the [current FE report](../../../UniNet_FE/docs/specs/project-github-integration.md). No GitHub permission or repository-link mutation was performed; 19 historical tables/associations/jobs fingerprint-match before/after. API-issued sessions independently prove the FE database target. Current API PID 13820; workers off.

Updated 2026-11-22 (Asia/Saigon). No GitHub consent, repository/identity linking, scoring or finalization performed by the agent.

## Important: target isolation incident and correction

The previous FE launcher used PowerShell dot assignment on `System.Data.Common.DbConnectionStringBuilder`. Because this type implements IDictionary, `ConnectionString` became a dictionary entry; subsequently setting `Database` did **not** change the string passed to the API. The previous runtime receipt's FE database label was therefore not proof of the API's actual target.

Observed reproduction: the old override still parsed to **uninet_github_app_e2e**; the fixed serialized string parses to **uninet_github_fe_e2e_20261121**. PostgreSQL logged a missing ChatRoomId column for API project creation, while the permitted FE clone has that column. Direct application-service diagnosis in an FE transaction succeeded and was rolled back. This exposed the runtime mismatch rather than a missing migration in the clone.

Before detection, API calls had filled missing profile fields for A/B, registered/filled profile C, and created login/refresh sessions **in the original E2E database**. A/B passwords were not reset. Project creation failed and did not leave a GITHUB_LINKING_TEST project there. No GitHub linking, evidence, snapshot, scoring or finalization mutation was sent. Earlier handoff statements that all live API verification was on the FE clone were incorrect and are superseded by this report.

The exact old API PID **2120** was stopped after verifying its executable/listener. No original database was queried directly, cleaned, restored or migrated to conceal/undo the incident. A targeted audit or reversal in the original database requires separate authorization; do not restore an entire old backup or delete an account blindly. The protected incident manifest is under `%LOCALAPPDATA%/UniNetGitHubFrontendSeed/20261121T180128888Z/incident-target-mismatch-manifest.json` and contains affected original account IDs without secrets.

Fix: [the existing launcher](../../scripts/start-github-frontend-e2e.ps1) now invokes the CLR `set_ConnectionString` / `get_ConnectionString` methods, parses/validates the **serialized effective child value** before starting, and records ConnectionTargetVerified. [A regression script](../../scripts/test-github-frontend-e2e-connection.ps1) proves target substitution and quoted password preservation with synthetic values. The seed additionally hashes the API-issued refresh token in memory and verifies its record in the permitted FE database before any fixture business write. Token/hash values are not logged. This proves the actual API persistence target independently of the receipt.

## Current runtime and database

- Backend: **PID 13820**, parent launcher **2104**, `http://localhost:5239`; API base `http://localhost:5239/api`. Health **200**.
- Expo: **PID 7644**, `http://localhost:8081`. Kept running; FE uses local API through its existing E2E launcher.
- Verified PostgreSQL: **127.0.0.1:55433 / uninet_github_fe_e2e_20261121 / uninet_test**.
- Container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`.
- Schema **104 applied**, exact current-source migration ID set, no migration applied in this task.
- E2E environment, root environment file skipped; global/manual/scoped Git workers and unrelated workers remain off. No active Git job.
- Runtime receipt `%LOCALAPPDATA%/UniNetGitHubFrontendE2E-runtime/runtime.json` has **ConnectionTargetVerified=true**. Live API-issued sessions are independently verified in FE DB by the seed.
- Current API logs: `%LOCALAPPDATA%/UniNetGitHubFrontendE2E-runtime/repository-access.out.log` and `.err.log`.

## Test accounts (real UniNet authentication)

| Persona | Email | FE profile ID | Actual role in Project A |
| --- | --- | --- | --- |
| A | student-a@uninet-e2e.test | fef4ef4d-0049-4f7a-b629-fe9760bbbff9 | Creator, inherent repository manager |
| B | student-b@uninet-e2e.test | 8791ff0a-38a4-44e2-97f3-3ef8fd0d2202 | Member, no Git Manager grant |
| C | student-c@uninet-e2e.test | ee920f44-7e77-4e2f-9f79-82c4c146d170 | Leader, active Git Manager grant |

A/B reused their existing accounts/passwords. FE C is account `a6c6db18-a584-4bc9-aa5b-df22d365e36b`, created through AuthService via the registration API, with the standard password hasher. All three are active Students and profileComplete=true. Missing Bio/UniversityName were filled via the profile API so FE does not redirect away to profile completion. Existing nonempty profile fields were preserved. No GitHub verification was seeded.

Passwords remain Windows CurrentUser DPAPI-protected outside Git:

- A: `%LOCALAPPDATA%/UniNetGitHubAppStep6E2E/student-a.dpapi`.
- B: **new store** `%LOCALAPPDATA%/UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi`.
- C: `%LOCALAPPDATA%/UniNetGitHubFrontendSeed/student-c.dpapi` (current user/SYSTEM ACL).

To log in, copy the chosen password locally without printing it:

```powershell
cd D:/uninet/UniNet_BE
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/copy-github-frontend-e2e-password.ps1 -Persona A
# Choose B or C for another persona. Paste into http://localhost:8081/login.
# Clear clipboard after use:
Set-Clipboard -Value ''
```

The agent did not run clipboard copying. Do not paste passwords into chat or commit credentials. Web sessions are tab-scoped: use separate browser profiles/windows or explicitly log out before changing persona; do not reuse A's bearer for B/C.

## Projects

### Project A — new linking fixture

- ID **44b9e967-a830-4061-b33b-5d96ca41df07**.
- Name **GITHUB_LINKING_TEST**; marker `UNINET_GITHUB_FE_SEED_V1` in description.
- Active; Creator A; three active membership rows (Creator A, Member B, Leader C).
- C's real grant ID **3cef6bdb-0aff-47ea-bba7-80f94abce220**; its existence, not the string Leader, authorizes Git management.
- **0 repository associations / 0 project Git identities** at handoff. No Git sync/scoring input created.
- Created via ProjectHub API. B/C applied to fixture recruitment and A accepted through real lifecycle APIs. Recruitment then closed; no friendship bypass or direct membership INSERT.
- The existing `official_project_workflow` database trigger created the new project's default OfficialV2 workflow row. This is not a Contribution Run, snapshot, peer evaluation or published score. The historical project's RequiresOptIn mode remains unchanged.

Open after login:

- Settings: `http://localhost:8081/group/44b9e967-a830-4061-b33b-5d96ca41df07?tab=settings`
- Members: `http://localhost:8081/group/44b9e967-a830-4061-b33b-5d96ca41df07?tab=members`

### Project B — retained connected fixture

- Existing ID **68f98a9f-d73d-4dcf-be5c-7099b29329c0**.
- Existing name **GitHub App E2E Test Project**, Completed. Not renamed or reopened.
- Creator A; B retained as Member. C was not added to this historical project.
- Association **25fe740d-dfbf-4859-8ce1-257247b4b3e7** -> real `trangtanduoc/uninet-github-app-e2e`, Active, linked; existing App installation **181414567**, stored grant not revoked.
- A/B's real historical identities retained: actor IDs **156803598** / **180517531**, logins **trangtanduoc** / **DatTanTruong**. No LinkedAt/OAuth timestamp backdating.
- Settings: `http://localhost:8081/group/68f98a9f-d73d-4dcf-be5c-7099b29329c0?tab=settings`.
- Members: `http://localhost:8081/group/68f98a9f-d73d-4dcf-be5c-7099b29329c0?tab=members`.

Do not unlink or change identity ownership in Project B while validating the new fixture. Its old evidence is valuable.

## API validation and preservation

Official login + auth/me passed independently for A/B/C. The returned session record was checked in the FE database; passwords and tokens were not logged.

| Project A read | A | B | C |
| --- | --- | --- | --- |
| Project detail and membership | 200, Creator | 200, Member | 200, Leader |
| Repositories | 200, empty | 200, empty | 200, empty |
| Project identities | 200, empty | 200, empty | 200, empty |
| GitHub connections | 200, 1 real | 200, 1 real | 200, 0 |
| App credentials (manager-only) | 200, 1 existing | **403** | 200, 0 |
| Published contribution | Unpublished | Unpublished | Unpublished |

No authenticated repository link mutation was sent. Project A OfficialV2 currently has no publication: UI must show Chưa có điểm, not use experimental results. Project B's historical workflow is RequiresOptIn and returns 409 for official-result reads. No official mode switch was performed on B.

FE preservation: **24 existing Completed Git jobs, 102 evidence versions, 693 observations, 5 snapshots, 4 Git Contribution Runs** remain. Full table fingerprints for historical Git/Peer/Contribution data passed. New fixture manager grant and the automatic new-project workflow row were accounted for explicitly; pre-existing rows in both tables were also checked, not blindly excluded.

Backups precede each seed invocation: custom-format pg_dump, pg_restore TOC check, SHA-256 and current-user/SYSTEM ACL, all under `%LOCALAPPDATA%/UniNetGitHubFrontendSeed/<UTC-run>/`. No backup was restored over any existing database.

Two completed replay validations returned **0 new fixture steps**. No duplicate account, project, membership or manager grant. Login sessions legitimately change each validation run. Initial failed attempts and the target incident are retained/documented, not reported as a clean first pass.

## Manual UI checklist

### 1. Creator A: new repository link

1. Log in as A; open Project A Settings.
2. Expect GitHub Repository empty state and Liên kết GitHub Repository.
3. Open dialog. A's real verified OAuth connection should be listed; an existing App credential may be reused if still valid. If reauthorization is required, choose A's GitHub account and perform real consent yourself.
4. Select existing authorized installation and the **test repository only**, then explicitly confirm linking. Do not select production/unapproved repositories.
5. Expected API: credentials -> installations -> installation repositories -> one App link POST -> refreshed association list.
6. Expected DB: a genuine Project A association referencing actual authorized installation/grant. No fabricated IDs. Worker-off means no automatic sync execution; pending/readiness states must remain truthful.

### 2. Member B: project identity

1. Log in as B; open Project A Members.
2. Expect Chưa liên kết với dự án on B's row and Liên kết GitHub của tôi; no control to verify another member.
3. B **already has a real OAuth connection globally**, so this is not a clean first-ever OAuth account. Select that connection to create B's project identity, or explicitly reauthorize yourself if needed. The agent did not disconnect B's valid history to simulate a fresh account.
4. Expected API: connections GET, then identities POST with connectionId only. Expected DB: B-owned project identity with actual numeric actor ID; current timestamps, no backdating.

### 3. Fresh OAuth persona C

1. Log in as C; open Project A Members -> Liên kết GitHub của tôi.
2. Expect no OAuth connections initially. Click Xác minh tài khoản GitHub and perform real GitHub login/consent yourself.
3. Expected: OAuth start -> provider -> Backend callback -> FE receipt `githubStatus=ready` -> complete -> connection now returned by GET.
4. Return to project and explicitly choose the verified connection. OAuth verification alone must not silently create a project identity.
5. A custom native scheme needs the appropriate dev build. This handoff's verified runtime is Expo Web.

### 4. Permission controls

- B -> Project A Settings: repository list visible; management controls absent; manager-protected credentials API rejects with 403.
- C -> Project A Settings: repository management controls present because the active persisted grant exists, even though C has no App credential yet. C must verify/authorize their **own** GitHub account; no use of A's session/credentials as C.
- A -> Project B Settings: existing association, privacy, authorization and sync status visible. Use this for read-only connected-state checks.

### 5. Conflict and attribution

- No fake conflict was inserted into live DB. Automated FE fixtures already cover conflict UI. For a real conflict, a user must first genuinely verify control of the same GitHub numeric account under another E2E persona and attempt a second ownership in the same project; Backend must reject. Do not manipulate actor IDs, LinkedAt or ownership rows.
- Historical attribution requires real evidence and policy eligibility. Existing old snapshots/runs are auditable but were not recalculated. New project has no synced evidence. Sync requires a separate authorized step; do not enable workers just to populate scores.

### 6. Contribution

- Each row has Contribution at the right; no published result means Chưa có điểm.
- Only own authorized Published API output may become a number. No fake zero or commit-count substitute. No leader review/finalization in this fixture setup.

## GitHub settings requiring user action

Verified live App: **UniNet Integration E2E**, App ID **5736376**, `https://github.com/apps/uninet-integration-e2e`. App JWT verification and existing installation GET both returned 200. Actual App webhook configuration is `https://outwit-salami-hug.ngrok-free.dev/api/webhooks/github/app`.

Existing ngrok public origin: `https://outwit-salami-hug.ngrok-free.dev` -> localhost:5239. No provider/tunnel setting was changed.

- In this E2E App's Developer Settings, inspect **Callback URL** for user authorization. Backend requires `https://outwit-salami-hug.ngrok-free.dev/api/github/app/authorization/callback`.
- In the OAuth App selected by the E2E file's `GitHub__ClientId`, inspect **Authorization callback URL**. Required: `https://outwit-salami-hug.ngrok-free.dev/api/github/oauth/callback`.
- The saved old callback values and the OAuth App display name were **not observable through the available App REST response**; do not claim them verified. Check the E2E Apps, never replace production callback settings.
- Webhook URL is not a callback URL. No need to change the verified App webhook for this test.
- FE OAuth return is `http://localhost:8081/github/callback`. App return-target `e2e` remains HTTPS Backend Swagger. Manual return + Kiểm tra quyền App is the current E2E workaround, **not** a completed automatic FE redirect. An automatic App return needs a separately configured verified HTTPS FE target; keep HTTPS validation.
- If the desired test repository is absent, use the E2E App installation's Configure page to grant it access to `trangtanduoc/uninet-github-app-e2e`, then return/recheck. No new GitHub repository/activity is needed. Existing installation metadata returned 200; repository discovery remains authoritative about actual current access.

Confirm a callback change by performing your own consent round trip, seeing the appropriate FE/Backend receipt and querying actual connections/credentials. A reachable callback rejecting missing state with 400 alone is not a successful authorization proof.

## Seed replay and transaction semantics

```powershell
cd D:/uninet/UniNet_BE
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/seed-github-frontend-e2e.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-github-frontend-e2e-connection.ps1
```

[Seed source](../../scripts/seed-github-frontend-e2e.ps1) checks container/role/schema/runtime/real API session persistence, backs up FE, and uses official registration/profile/project/recruitment/acceptance/Git Manager APIs. Every API mutation retains its existing transaction/rollback policy; membership and grant transitions use the existing Serializable transactions. This is a **journaled resumable multi-request seed, not one transaction spanning all HTTP calls**. On error it stops, retains committed earlier units and requires state inspection before replay; it does not pretend to roll back a remote account creation or blindly retry an ambiguous POST.

Current manifest `%LOCALAPPDATA%/UniNetGitHubFrontendSeed/manifest.json` contains IDs/roles/checks and credential file paths, no secrets. Each completed future run also saves a manifest in its protected receipt directory. Marker/title/creator checks recover a committed project if an HTTP response was lost. Existing GitHub facts are never seeded or overwritten.

## Cleanup — proposal only, NOT executed

Keep fixtures for manual testing. Before cleanup, back up FE and inspect the manifest and any manual actions since handoff.

1. Restrict to database uninet_github_fe_e2e_20261121. Identify new Project A ID above and C's FE account/profile IDs, not similarly named records in another DB.
2. Revoke C's **Project A** manager grant through the existing authorized API if retiring delegated access.
3. If users subsequently linked real repositories/identities, use normal unlink operations with appropriate identity owners; retain audit. Do not delete shared provider objects, evidence, snapshots or runs. If immutable provenance/publications now reference the fixture, do not hard-delete its parents.
4. Review accepted request/membership and generated notification records before using the normal project removal lifecycle. If FK/audit guards reject removal, retain/retire the fixture rather than disabling constraints.
5. Remove/deactivate C only through a supported account-administration lifecycle after checking their new OAuth, membership and audit dependencies. No automatic SQL DELETE is supplied. Preserve A/B, Project B and all historical Contribution/Git data.
6. Only after approved retirement remove the local C DPAPI file. Do not delete A/B's existing stores. Original-database incident cleanup is a **separate approval**, not this FE fixture cleanup.

## Status

**FE fixture data READY for manual testing.** Provider-side callback consent remains conditional; first-ever OAuth is available with C; B's existing OAuth was intentionally preserved. Ownership conflict is fixture-tested, not live-fabricated. New sync/scoring/finalization remain out of scope. The original-target incident is disclosed and requires user review; no claim of an entirely side-effect-free execution.
