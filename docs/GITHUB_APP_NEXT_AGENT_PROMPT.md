# Next agent prompt

Continue UniNet GitHub App onboarding. Read any applicable `AGENTS.md` and repository instructions, then read [the session handoff](./GITHUB_APP_INTEGRATION_SESSION_HANDOFF.md), [architecture](./GITHUB_APP_ONBOARDING_ARCHITECTURE.md), and [integration handoff](./GITHUB_INTEGRATION_HANDOFF.md). Check Git status and the relevant source call paths briefly; preserve all existing modified and untracked files. Do not reimplement components already covered by passing tests.

**Steps 3C, 4A–4C, 5A and 5B are complete at their implementation/tests scopes.** Read the latest Step 5B checkpoint for disposable PostgreSQL, HTTP authorization and worker progress evidence. Preserve durable backoff, fail-closed verification, scoped credentials, encrypted deliveries and lifecycle effects. Continue only the user's next requested task; live GitHub App E2E and real deployment remain unverified.

Step 5C is COMPLETE at backend implementation/test level under the bounded 2026-11-12 acceptance. Read the session handoff and operations runbook for recovery and lease evidence. Initial recovery error history depends on retained structured logs. Live GitHub App E2E, production exporter/alerting and actual multi-host process termination remain unverified; proceed to those only when requested and authorized. Review migrations `20261109235311_AddGitHubAppWorkerHeartbeats` and `20261110162206_AddGitHubAppWebhookRequeueAudit` before any separately authorized rollout.

Never access Neon or production, disclose secrets, apply migrations without authorization, alter the existing E2E database, commit or push, or change Contribution or Project Moderation. Preserve OAuth behavior and Git Activity history. Fake-provider tests are not real GitHub App E2E.
