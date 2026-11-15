# UNINET — next agent: Experimental Git Contribution MVP

## Status

The end-to-end Experimental Git Contribution MVP is implemented in the current dirty worktree. Read [handoff](UNINET_GITHUB_CONTRIBUTION_HANDOFF.md) and [decisions](UNINET_GITHUB_CONTRIBUTION_DECISIONS.md), then inspect `git status --short --untracked-files=all` before editing. Preserve all tracked and untracked changes; do not repeat GitHub integration, atomic writer, snapshot, or MVP implementation.

The MVP has `ExperimentalGitScoringV1`, snapshot-level evidence eligibility and historical attribution, frozen roster/identity/peer/evidence inputs with hash, explicit policy acknowledgment, duplicate/overlap handling, leader review/finalize, member results, and additive persistence/API. It is not official Contribution scoring and does not change legacy v1.

## Validation already completed

- Focused MVP, evidence/attribution, legacy Contribution regression, and PostgreSQL HTTP flow: **54 PASS / 0 FAIL / 0 SKIP**.
- PostgreSQL 17 disposable database: all 82 migrations applied; snapshot capture/read, run lifecycle, endpoint authorization, and finalized-run database trigger passed.
- `dotnet build UniNet.sln`: previously PASS, one existing `CS9113` warning in `ProjectsController.cs`; current test run compiled all projects to temporary artifacts.
- `git diff --check`: PASS.
- Build/test output used a temporary artifacts directory.
- The disposable container was stopped and removed. No E2E/Neon/production/shared database or live GitHub was used.

## Immediate next action

No additional implementation or test phase is required by this handoff. Wait for the user's frontend/real-project feedback, then fix only reproducible MVP defects with focused regression tests. Live GitHub behavior and production readiness remain unverified. Never use E2E port `55433`, Neon or production. Do not run live GitHub sync without explicit direction.

## API

- `POST /api/projects/{projectId}/contribution/git-experimental/runs`
- `GET /api/projects/{projectId}/contribution/git-experimental/runs/{runId}` (project creator only under current policy)
- `POST /api/projects/{projectId}/contribution/git-experimental/runs/{runId}/review` (project creator only under current policy)
- `POST /api/projects/{projectId}/contribution/git-experimental/runs/{runId}/finalize` (project creator only under current policy)
- `GET /api/projects/{projectId}/contribution/git-experimental/results` (project participants; finalized run only)

Create requires `AcknowledgeExperimentalPolicy=true`. Repeating a create for the same project/snapshot/policy returns its existing run. Review decisions include approve or request revision; only approved runs can be finalized. No public raw-evidence endpoint exists.

## Safety and working style

Feature-first, risk-based testing. Preserve all worktree changes. Never reset/stash/delete unrelated changes, rewrite existing migrations, commit/push, restart the backend, modify watchdog/launcher, run live sync, or access E2E/Neon/production data. Disposable PostgreSQL and HTTP flows passed; do not claim live GitHub or production paths as passing. Do not describe Experimental V1 as official or silently promote incomplete source evidence.

## COPY-PASTE START PROMPT

```text
Read the Contribution handoff and decisions, then inspect git status. Preserve every existing change. The Experimental Git Contribution MVP is implemented; do not redo completed work or start a separate acceptance phase. Wait for real-project test feedback and fix only reproducible defects with focused tests. Never use E2E/production databases, run live GitHub sync, restart services, or commit/push without explicit direction.
```
