# Notification inbox reference redesign — 2026-11-06

- Spec: [notification-inbox-redesign](../specs/notification-inbox-redesign.md).
- `GET /api/notifications` adds optional `readOnly=false`. When true, filters `ReadAt != null` before counting/pagination. Existing unread/category filters and global `unreadCount` are preserved. Passing both readOnly/unreadOnly yields no rows.
- `GetMineAsync` appends the new optional argument after CancellationToken so existing positional service callers retain their meaning. No response shape or schema changes.
- FE and Dashboard now show All/Unread/Read tabs, real total/unread badges, compact category rows, relative time beneath content, unread dot/read check, and Today/Earlier groups based on local midnight. FE provider obtains global totals from a pageSize=1 list query.
- Existing optimistic read/rollback, background PATCH, global error visibility, safe navigation and no success-toast behavior are preserved. No fabricated New badge is shown without metadata.
- Verification: 31 focused backend notification tests pass using the existing isolated harness. New regression verifies read-only filtering before pagination and preservation of global unread count. Dashboard production build, FE six hook/six utility tests and both Chrome suites pass, including tabs, counters, responsive widths, realtime, pagination and optimistic error recovery. Screenshots inspected at desktop and 320/375px.
- Full backend test project still has unrelated AI moderation interface compilation failures. FE full TypeScript check has existing errors in opportunities and AssessorProfilePosts; no notification errors remain after replacing an unsupported iOS symbol. Native-device testing not performed.
- Deploy the readOnly backend query support with the updated clients; otherwise Read tab pagination cannot be correct against older servers.
