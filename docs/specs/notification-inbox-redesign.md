# Notification inbox reference redesign — 2026-11-06

## Acceptance and design
- Apply the supplied reference to FE and Dashboard inboxes: compact white/light-blue panel, three underlined tabs All/Unread/Read, real total/unread badges, right-aligned mark-all action, date headings, separated rounded rows.
- Unread rows use light blue fill and a blue dot. Read rows use a subtle border and double-check icon. Colored rounded-square category icons, bold titles, body text and relative time below content match the reference hierarchy. No ellipsis menu or success toast.
- Preserve optimistic reads, rollback/error across navigation, safe targets, realtime, pagination and account isolation. Do not invent a promotion badge for data without matching metadata.
- Read-only filtering must happen before server pagination. Add optional `readOnly` query while preserving existing API defaults and positional service callers. Badges use global totals from API, not the loaded page length.

## Plan and verification
1. Add backend readOnly filter and regression proving totals/pagination with unread rows preceding read rows.
2. Extend notification DTO filters/services/providers with readOnly and totalCount. Restyle FE panel/rows and Dashboard page/rows using existing Symbols/Lucide and spacing conventions; no dependencies.
3. Update browser fixtures for tabs, read-only filtering, true counts, responsive layout and optimistic rollback. Run focused backend tests, FE unit/type checks, Dashboard build and both Chrome suites; inspect screenshots.
- Backend: `dotnet test artifacts/notification-audience-check/NotificationAudienceCheck.csproj --no-restore --verbosity quiet -p:UseSharedCompilation=false -m:1 -nr:false` (isolated harness while unrelated test compile errors persist).
- Dashboard: `npm run build`; `node tests/notifications-browser-check.cjs`.
- FE: `node tests/notification-open.test.cjs`; `node tests/notifications.test.cjs`; `node node_modules/typescript/bin/tsc --noEmit`; `node tests/notifications-browser-check.cjs`.
- Scope: notification service/controller, client inbox components/providers/services/types, tests and handoff. No migration, financial changes or deployed data changes.
