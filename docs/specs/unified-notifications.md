# Unified notifications

## Objective and audit
Implement the attached October 25, 2026 request across Backend, Expo frontend and Dashboard. Exactly one recipient notification table, `Notifications`, upgrades `VerificationNotification` in place. No production migration or deployment is authorized.

PostgreSQL/Npgsql EF Core 8; all IDs UUID. `Users` is a DbSet alias for `UserProfiles`, NOT Accounts. Existing RecipientUserId references UserProfiles.Id. New recipient/sender IDs reference Accounts.Id, matching JWT `sub`. Migration must join UserProfiles.AccountId and preserve notification IDs/content/timestamps. Existing target constraint allows exactly one of three targets; migrate that target before removing old columns. Retain old API and CLR target accessors for compatibility.

Actual roles: Student=0, Partner=1, Admin=2, Moderator=3, Verificator=4. There is no generic User role and no existing permission registry/grant model. Add a small explicit notification permission policy based on existing business role authorities, with per-account grant/deny overrides; no fabricated existing permission names. Admin review actions and target resolution must honor revocation. Existing workspace SignalR hub is Student-only and channel-specific, so use a separate authenticated notification hub sharing existing JWT authentication. Send invalidations only, never private content on public groups.

## Structure and contracts
- Domain: extend existing CLR VerificationNotification, map to Notifications; add broadcast job, selected-recipient relation, permission overrides and durable delivery outbox. These are jobs/authorization/delivery records, not parallel notification stores. Do not store recipient or read lists as JSON.
- Application: NotificationService, permission policy, broadcast service; enqueue within existing business SaveChanges transaction.
- API: /api/notifications list/count/read/read-all/target; legacy /api/verification-notifications adapter; /api/admin/notification-broadcasts drafts/preview/publish/history/status/cancel and /permissions. DTOs, ownership and active account checks on all operations.
- SignalR: /hubs/notifications, JWT sub user ID; NotificationChanged {notificationId?,reason}. DB is authoritative, reconnect resync.
- Frontends: dedicated Student/Partner inbox pages; Dashboard inbox/broadcast management. The bell opens the inbox page directly, without a popup. Read and target validation precede navigation; route allowlists, account cleanup, loading/error/empty states.

List response: {items,page,pageSize,total,unreadCount}. Count: {count}. Read endpoints return 204. Target resolution: {targetUrl}. Broadcast audiences All/Role/Selected; statuses Draft/Queued/Processing/Completed/Cancelled/Failed. Every recipient gets one notification; unique (RecipientUserId,EventKey) where key is not null. Broadcast processing batches with durable status, retry and idempotency; HTTP only queues work. Outbox inserted transactionally, retried by worker after commit.

## Ordered tasks and acceptance
1. Core schema and migration: preserve sample old rows, map distinct profile/account IDs, reject invalid old targets; model and migration tests.
2. Inbox and permission API: tests prove cross-account isolation, read idempotency, expiry/filter/count and permission revocation.
3. Broadcast + outbox + SignalR: tests prove dedup, common broadcast ID, retries and user routing; batches persist with business transaction.
4. Existing business events: account verification, application decisions/submission, project invitations/join/task assignment, moderation where present. Do not invent absent workflows. Regression tests for existing verification.
5. Student/Partner frontend: account reset, reconnect sync, safe target routing, direct bell-to-page navigation and responsive inbox page; focused tests/browser checks.
6. Dashboard: own inbox plus permission-gated broadcast creation/preview/publish/status/cancel; build/lint/browser checks.
7. Review and handoff: document exact integration gaps, actual command results, migration rollout and risks.

## Continuation acceptance (October 25)
- Missing recruitment resources return `TARGET_UNAVAILABLE` (404), including recipients without a profile; existing student/partner application targets still resolve.
- Dashboard history can reopen a saved Draft or retry a Failed broadcast after a fresh recipient preview and a separate explicit publish action. Reuse the broadcast ID; never create a replacement job or publish on preview.
- Verify backend notification tests, dashboard browser recovery flow/build/targeted lint, and Student/Partner browser flows; document unrelated baseline failures and PostgreSQL verification limits in handoff.

## Commands and verification
Backend: `dotnet build UniNet.sln`, `dotnet test UniNet.Tests/UniNet.Tests.csproj`; PostgreSQL fixtures when configured; `dotnet ef migrations script --project UniNet.Infrastructure --startup-project UniNet.API`. FE: `node node_modules/typescript/bin/tsc --noEmit`, `node node_modules/expo/bin/cli export --platform web`; focused node tests. Dashboard: `npm run build`, `npm run lint`.

## Boundaries
Follow existing concise C# service conventions and explicit DTOs. Preserve user changes in FE profile files. No unrelated refactoring, no secrets, no production schema application. Tests use disposable local databases only. Record unavailable environment verification honestly. Source references: Microsoft SignalR auth/user identifier docs and Npgsql EF index docs.
