# Unified notifications — October 25, 2026

## Current implementation
- Spec: `docs/specs/unified-notifications.md`. Recipient rows upgrade the existing verification notification table to `Notifications`; recipient/sender IDs use `Accounts.Id` rather than profile IDs.
- Account inbox/count/read/read-all/target APIs enforce ownership, active status, expiry and current permissions. The legacy verification API remains available.
- Business notifications cover verification, recruitment, project invitations/join decisions/task assignment and community moderation. Inserts and durable invalidations participate in the business save transaction.
- Broadcasts persist drafts, audiences, progress and retry state; the worker processes at most 250 accounts per batch. SignalR sends per-account invalidations; clients refetch authoritative APIs.
- Student/Partner and Dashboard clients have inboxes, bells, read actions, target allowlists, account cleanup and reconnect synchronization. Dashboard includes preview/publish/cancel plus reopening saved drafts and retrying failed jobs.

## This continuation
- Fixed null resource comparisons in `NotificationTargets`: missing applications/jobs for accounts without profiles now return `TARGET_UNAVAILABLE` (404), rather than a null reference (500) or a fabricated jobs route.
- Added four regression cases, verified failing before the fix. Recruitment tests also check valid owner navigation for both application decisions.
- Extended batch tests to simulate failure after 250 deliveries, republish the same job, and finish at 252 unique notifications/outbox records without resetting progress.
- Dashboard recovery requires a fresh preview and separate publish confirmation, reuses the existing broadcast ID and explains partially delivered retries. Browser tests cover saved drafts after reload, preview failure, focus and 320px layout.

## Verification
- `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --filter FullyQualifiedName~Notification --verbosity quiet -m:1 -nr:false -p:UseSharedCompilation=false`: 32 passed, 5 skipped. `UNINET_TEST_POSTGRES` is unset; PostgreSQL migration/concurrency tests remain unverified.
- Generated migration SQL successfully with `dotnet ef migrations script 20261024095804_AddWorkspaceChat 20261025072442_UnifiedNotifications --no-build --project UniNet.Infrastructure --startup-project UniNet.API --output UniNet.Infrastructure/obj/notifications-migration.sql`.
- FE pure notification tests, TypeScript check and Expo web export pass. Student/Partner Chrome fixture tests pass against Expo dev, including realtime/reconnect, account replacement and 320/768/1440px widths.
- Dashboard build, target tests, notification browser tests, session refresh browser tests and ESLint for the changed page pass. Browser APIs/WebSockets are local fixtures; these are independent client checks, not a deployed backend end-to-end test.
- Chrome DevTools MCP could not attach because its profile was already in use. Existing Playwright scripts ran real Chrome headless instead.

## Existing failures / limitations
- Full backend baseline before this fix: 549 passed, 27 skipped, 5 failed. Focused rerun confirmed: transaction HTTP route expects 200 but gets 404; skills category constraint expectation differs from nullable schema; partner registration stores organization instead of representative name and accepts two invalid-name cases. These failures are outside the notification changes.
- Dashboard full lint still reports `AuthContext.tsx` effect/FastRefresh and `SidebarItem.tsx` effect errors. Production bundle has the existing >500 kB advisory.
- FE static-export `/login` at desktop width raises React hydration error 418 before authentication; the viewport-dependent auth transition is a likely cause. Notification flows pass on Expo dev; static-host browser validation is not clean.
- Staff target pages still use existing list/mock screens; notification integration does not implement missing review/detail business screens.

## Migration / rollout boundary
- No database migration or deployment was applied. Before rollout, back up the database and run PostgreSQL fixtures against an isolated test database with representative legacy rows, including distinct profile/account IDs.
- Review the generated SQL: it rejects unmappable recipients and ambiguous legacy targets, preserves legacy rows/IDs, maps recipients through profiles, then creates outbox/broadcast/permission support tables.
- Apply the reviewed schema before starting the new API/worker. Configure JWT/CORS/reverse proxy for `/api` and `/hubs/notifications`; deploy both clients against that API and validate with separate recipient accounts.
- Down migration deliberately refuses rows containing new notification history/read/delivery metadata that the old schema cannot preserve. Export/migrate those records or restore a coordinated backup instead of forcing a rollback.
