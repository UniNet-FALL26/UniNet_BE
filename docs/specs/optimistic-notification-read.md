# Optimistic notification reads — 2026-11-06

## Behavior
- Dashboard and FE mark an unread row read and decrement the badge immediately on click.
- Read PATCH runs in the provider, independent of page navigation/unmount. No success toast.
- A safe embedded target opens immediately; otherwise resolve a related target independently of PATCH. Notifications without a target only change read state.
- PATCH failure restores the original unread state/count and displays an error globally, including after navigation. Session changes must discard old results.
- Pending read requests deduplicate by notification ID. Refreshes started before/during pending reads cannot overwrite the optimistic badge. Lists project provider read overrides, including unread-only views and revisiting pages.

## Implementation plan
1. Extend each session-scoped NotificationsProvider with read overrides, background read, rollback and global error reporting. Reuse FE ToastHost; add Dashboard error alert in its provider.
2. Project overrides in both inboxes and decouple target navigation from read completion. Preserve safe target allowlists and account lifecycle guards.
3. Add delayed/failing PATCH browser regressions for instant row/badge changes, rollback, navigation and no success messages. Update focused hook tests. Verify Dashboard build, FE type check/export and browser fixtures; record unrelated failures.

## Commands and scope
- Dashboard: `npm run build`; `node tests/notifications-browser-check.cjs`.
- FE: `node tests/notification-open.test.cjs`; `node tests/notifications.test.cjs`; `node node_modules/typescript/bin/tsc --noEmit`; `node tests/notifications-browser-check.cjs`.
- Follow existing React/React Native context and service conventions; no backend API/schema changes or dependencies. Keep changes limited to notification reads and their regression coverage.
