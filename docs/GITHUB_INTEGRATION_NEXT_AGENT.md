# GitHub Integration — Next Agent Quick Start

**Checkpoint:** 2026-11-05 · **Phase 3:** PARTIAL; real E2E not started.

## Read first

1. [GITHUB_INTEGRATION_HANDOFF.md](./GITHUB_INTEGRATION_HANDOFF.md) — approved rules, phase status, migration history, test evidence and outstanding work.
2. [backend.instructions.md](../.github/instructions/backend.instructions.md) — required before backend code changes.
3. [PHASE3_E2E_LOCAL_SETUP.md](./PHASE3_E2E_LOCAL_SETUP.md) — local E2E setup and safe start procedure.
4. [GITHUB_INTEGRATION_PHASE3_E2E_REPORT.md](./GITHUB_INTEGRATION_PHASE3_E2E_REPORT.md) — verified results and blockers.

## Current verified state

- Branch `dattt`, HEAD `cdf2bf153d602f1c351f9bd365a4705f43e14678`.
- Worktree is intentionally dirty: 6 modified tracked files and currently 95 untracked files (the count was 94 before this quick-start doc was added). Phase 1–3 implementation, restored migration `20261104155132_RestoreProjectGitMemberIdentities`, later migrations, E2E setup, and docs are among them. Inspect before any edits. Do not stage broadly, overwrite/delete, stash, reset, clean, migrate-remove, commit or push.
- Latest recorded solution build passed with isolated output. Latest focused Phase 1–3/membership run passed 48 tests. These use local/fake or SQLite behavior and do not validate PostgreSQL worker locking or live GitHub.
- `dotnet ef migrations has-pending-model-changes` reported no pending model changes. Source migration chain was inspected; no database migration was applied by the agent. Database applied status is unknown.
- Docker reports `uninet-postgres-e2e` using `postgres:17` at `127.0.0.1:55432`; TCP responds. The database was not authenticated to, so its actual server version, target identity, schema and migration history are unverified.
- `.env.e2e` exists and is ignored. Its values are private; do not print them. Current validation previously failed due placeholders. API port 5239 is occupied by `UniNet.API` PID 8328; do not kill it. The launcher refuses to start while the port is busy.
- No test OAuth credentials or public webhook URL are configured/verified. No live GitHub request has been made.

## First action

1. Re-run `git status --short`, `git diff --stat`, branch and HEAD. Preserve the dirty worktree.
2. Coordinate with the user about the existing API process and local-only E2E values. Do not stop PID 8328 without explicit coordination.
3. Run `scripts/check-phase3-e2e.ps1` only after local placeholders are filled. It checks configuration, Docker metadata and TCP only; it does not authenticate to PostgreSQL or GitHub.
4. Before connecting or migrating, independently verify the connection targets only the disposable local database `uninet_e2e` on port 55432, then inspect its migration history. Abort if target isolation is uncertain. Never use Neon/shared/production.
5. Run `scripts/start-phase3-e2e.ps1` only after the checker passes and port 5239 is free through coordinated shutdown. It does not apply migrations.
6. For GitHub scenarios, use only a test OAuth App and disposable test repository; add a webhook only through a test HTTPS tunnel. Do not use production credentials, domain or repository.

## Stop conditions and scope

- If PostgreSQL target identity or disposability cannot be proven, stop before connecting/applying migrations and report BLOCKED.
- If OAuth credentials, test repository, or public webhook URL are unavailable, complete only the safe local checks and report GitHub E2E as BLOCKED.
- Do not start Phase 4, change Contribution behavior, modify finalized runs, or expand scope beyond Phase 3.
- Do not run `dotnet ef migrations remove`; do not apply migrations to Neon or any shared database.

## Completion criteria

Report PostgreSQL version and verified database identity; migration/schema/index/FK and job-claim/recovery results; test OAuth, repository and identity linking; initial/manual/scheduled sync; real activity API visibility; webhook and duplicate handling; access revoke/recovery; focused tests/build; and outstanding defects. Do not claim Phase 3 complete unless the required real PostgreSQL and GitHub E2E checks pass. Update the E2E report and handoff, then stop before Phase 4 pending approval.
