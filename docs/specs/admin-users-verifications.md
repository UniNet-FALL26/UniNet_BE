# Admin users and account verification

## Audit / decisions
- Reuse Accounts (JWT subject), Users/UserProfiles (profile IDs), UserVerifications and Notifications. There is no Super Admin or general permission registry; existing per-account NotificationPermissionOverrides and NotificationPermissionPolicy provide the reusable authorization mechanism. No second account/verification store.
- Student verification already has private multipart documents, history rows, conditional review, rejection/resubmission and notifications. Partner verification has JSON/uploaded documents and decisions, but only pending list/detail and no atomic competing-decision protection. Partner role/account exists at registration; approval sets Profile.IsVerified and unlocks existing verified-partner business guards, rather than creating another account.
- VerificationStatus is Pending/Approved/Rejected/Cancelled. No changes-requested state exists; do not invent one. Rejection with a reason and resubmission remain supported.
- Dashboard staff/student/company views are static mock pages, with no reusable table/dialog/toast library. Keep existing blue/white components and add small shared management UI.
- Keep only the header bell for personal notifications, per user's explicit clarification. Preserve existing routes; add real users and review routes/aliases, permission-filtered sidebar sections and real pending badges.

## Contracts and boundaries
- Extend existing permission naming with users.view/detail/lock/unlock, staff.manage and permissions.manage; retain verification.student.review / verification.partner.review for the existing review workflows, with catalog and per-staff overrides. Defaults follow existing Admin authority. No role elevation API; prohibit self permission edits and locking any staff/Admin account.
- Add Account.SessionVersion (default 0) and append-only AccountStatusChanges for lock reason/actor/history. Validate active status and session version on all JWT requests; revoke refresh tokens on lock. Missing legacy version claim means 0. New login uses current version; unlock never revives older JWTs.
- Reuse /api/admin/student-verifications and /api/admin/partner-verifications: add searchable/sortable paginated lists, safe detail/history, stats; preserve existing student/public partner contracts. Extend partner reviews transactionally and conditionally; add a protected document endpoint for legacy external documents without fetching arbitrary URLs server-side.
- New /api/admin/users list/stats/detail/lock/unlock; explicit DTOs, search/role/status/verification filters, stable sorting; /api/admin/management capabilities and pending counts; permission catalog/effective overrides via /api/admin/permissions. Staff creation retains /api/staff/accounts.
- Backend checks permissions on every action, including private document access. Staff review access must be explicitly granted. New notification targets resolve users and exact verification routes after rechecking permission. Lock notices persist while the owner is suspended and become readable after unlocking.
- Never apply production migrations, deploy, delete account data, expose refresh/password/private document bytes in JSON or commit tests/docs unless separately requested. No new dependency required.

## Ordered slices / acceptance
1. Account authorization and audit schema (<=5 source files): model mapping, migration; service tests prove lock/unlock, no self/staff lock and permanent session revocation.
2. Users API/permissions: catalog, DTO/service/controller; tests prove filtering, pagination, safe detail, permission denial and audit/notifications atomicity.
3. Review queries and partner concurrency: reuse existing services/controllers; tests prove all-status search, private documents, unchanged Student workflow and only one competing Partner decision.
4. Dashboard users/details and staff permissions: typed API service, capabilities provider and responsive pages; build/browser tests for access, filters, confirmations and error states.
5. Dashboard review pages/sidebar/deep links: real lists/details/documents/history/decisions, notification routes and pending counts; browser tests for both types and permission gating.
6. Final regression/security review and concise handoff with actual results and rollout limits.

## Implementation style / verification
All six slices implemented and reviewed. Actual validation, baseline failures, rollout limits and file inventories are recorded in `docs/handoffs/admin-users-verifications.md` and the Dashboard handoff. Production migration/deployment remains explicitly excluded.

Follow existing primary-constructor C# services and explicit record DTOs, e.g. `await NotificationPermissionPolicy.RequireAsync(db, actorId, permission, ct);`. Use EF queries with bounded paging and deterministic secondary ID sort; transaction + conditional updates for decisions/status transitions. React uses typed API clients, abortable effects, loading/error/empty states and accessible forms/dialogs.

Backend: `dotnet build UniNet.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`; `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`; focused management/auth/verification tests first. EF generates SQL only; PostgreSQL tests require isolated UNINET_TEST_POSTGRES. Dashboard: `npm run build`, targeted ESLint, existing notification/session tests plus management browser fixtures. Preserve known unrelated baseline failures and report them honestly.
