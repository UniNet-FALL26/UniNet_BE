# Admin users and verification — handoff

## Delivered
- `/api/admin/users` defaults to Student/Partner only, including search/filter/pagination totals and statistics. `staffOnly=true` selects Admin/Moderator/Verificator for the separate Staff page; a role filter cannot widen either scope.
- Real user list/detail, bounded search/filter/sort/pagination, statistics, public profile, verification and account status history.
- Lock/unlock requires a reason; disallows self and staff/Admin targets. Conditional updates prevent competing transitions. Audit, refresh-token revocation and notifications share a transaction.
- SessionVersion invalidates old JWTs permanently across unlock. HTTP and SignalR invocations validate account status/version; passive sockets are checked in batches every five seconds.
- Student and Partner review lists/details/statistics use existing verification rows, private documents and histories. Partner submission/review is serialized/conditional; a second decision returns 409. Approval verifies the existing Partner profile, never creates another account.
- Existing notification permissions extended for users/staff. Staff creation reuses the existing endpoint; bulk permission changes are atomic and cannot elevate staff to Admin-only capabilities.
- Dashboard uses actual typed API clients. Browser tests use isolated fixtures, not production data. Personal notifications remain accessible only through the header bell.

## API
| Method | Route | Access |
|---|---|---|
| GET | `/api/admin/users`, `/stats` | users.view |
| GET | `/api/admin/users/{accountId}` | users.detail |
| POST | `/api/admin/users/{accountId}/lock`, `/unlock` | users.lock / users.unlock; JSON reason |
| GET | `/api/admin/management` | Active staff; effective permissions and permission-gated pending counts |
| GET / PUT | `/api/admin/permissions/{accountId}` | Admin + permissions.manage; PUT array of permission/granted |
| POST | `/api/staff/accounts` (existing) | Admin + staff.manage |
| GET | `/api/admin/student-verifications`, `/stats`, `/{verificationId}` | verification.student.review |
| POST | `/api/admin/student-verifications/{verificationId}/approve`, `/reject` | verification.student.review |
| GET | `/api/student/verification/{verificationId}/document` | Owner or authorized reviewer |
| GET | `/api/admin/partner-verifications`, `/stats`, `/{verificationId}` | verification.partner.review |
| POST | `/api/admin/partner-verifications/{verificationId}/approve`, `/reject` | verification.partner.review |
| GET | `/api/admin/partner-verifications/{verificationId}/documents/{index}` | Authorized reviewer; private file |
| GET | Same document route + `/external` | Authorized reviewer; validated legacy HTTPS URL, no server-side fetch |

New permission names: `users.view`, `users.detail`, `users.lock`, `users.unlock`, `staff.manage`, `permissions.manage`. Admin defaults grant these; Moderator/Verificator need explicit user/review grants. Review permissions cover both viewing and deciding. The backend enforces permissions independently of sidebar visibility. Admin-only permissions cannot be assigned to staff; self permission edits and editing Admin accounts are rejected.

## Notifications
Account lock/unlock records a private owner notice targeting their profile/settings. Suspended owners cannot access the API; the persisted lock notice becomes readable after unlock. Existing Student/Partner submission and approve/reject notifications are reused. Staff deep links point to exact verification detail routes and recheck access; account targets link authorized staff to user detail. Existing delivery/outbox behavior remains in use.

## Schema / deployment
Migration `20261025101348_AdminUserManagement` adds `Accounts.SessionVersion` default 0 and `AccountStatusChanges` with account/actor foreign keys and account/time index. No account or verification data is recreated. Context prevents application updates/deletes of status audit records.

Generated SQL: ignored `UniNet.Infrastructure/obj/admin-management-migration.sql`. Model drift check reports none. Migration has NOT been applied. Down refuses to discard audit rows or nonzero session versions; rollback after use needs a deliberate history-preserving strategy and retirement of affected JWTs.

Before production: back up database; review SQL; run isolated PostgreSQL migration/concurrency tests with `UNINET_TEST_POSTGRES`; apply migration to staging before starting new API; validate JWT/refresh/SignalR and private documents with actual staff/student/partner accounts; verify frontend API URL/CORS, permissions and notification links; resolve or explicitly triage baseline failures below; then schedule production migration and coordinated API/dashboard rollout. Do not revert to an API that ignores SessionVersion while revoked JWTs remain valid.

## Verification (2026-10-25)
- Backend build succeeded; existing ProjectsController unused-parameter warning remains.
- Final complete suite: 594 total, 561 passed, 28 skipped, 5 failed. PostgreSQL integration environment is unset; no production database was touched.
- Focused management/notifications/Student/Partner suite: 50 passed, 6 skipped. Covers real HTTP authorization/session invalidation, live socket revocation, private metadata, atomic permissions, Partner competing decisions and one notification/no duplicate account.
- Five pre-existing failures: TransactionHttpTests.AllFiveRoutesBindAndUserRoutesRequireStudentWhileWebhookAllowsAnonymous (404 vs 200); AuthTests.PartnerRegistrationSeparatesOrganizationAndPersonalNames (organization vs personal name); two AuthTests.InvalidPartnerNamesDoNotCreateAnAccount cases (current registration contract ignores personal-name fields); DatabaseDesignTests.SkillsUseExactlyFourCategories (nullable category check). Unrelated contracts were preserved.
- Dashboard build and targeted lint passed. Full lint retains three existing errors in AuthContext.tsx and SidebarItem.tsx. Real Chrome management fixture suite passed at 320/768/1440px; notification/session regression suites and target tests passed. FE notification target tests (6) and TypeScript passed.
- Review covered permission boundaries, parameterized search, DTO privacy, transaction/conditional updates, pagination, batch permission/socket queries and safe deep links. Actual deployed API/database end-to-end validation remains a staging step.

## Changed files for this feature
New: `UniNet.Domain/Entities/AccountStatusChange.cs`; `UniNet.Infrastructure/Data/AdminManagementConfiguration.cs`; migration and Designer; `UniNet.Application/Services/AdminManagementContracts.cs`, `AdminUsersService.cs`, `AdminPermissionService.cs`, `AdminVerificationQueries.cs`; `UniNet.API/Controllers/AdminUsersController.cs`; `UniNet.API/Services/AccountTokenValidation.cs`, `AccountSessionConnections.cs`; tests `AdminManagementTests.cs`, `AdminReviewConcurrencyTests.cs`; spec and this handoff.

Updated: Account.cs, UniNetDbContext.cs, model snapshot, Program.cs, AuthService.cs, NotificationPermissionPolicy.cs, NotificationService.cs, NotificationTargets.cs, VerificationNotifications.cs, StudentVerificationService.cs, PartnerVerificationService.cs, StudentVerificationController.cs, PartnerVerificationController.cs. Extended existing notification HTTP, Partner notification and conditional PostgreSQL migration tests. Other pre-existing working-tree edits are outside this feature.

## Deliberate limits
Existing statuses remain Pending/Approved/Rejected/Cancelled; no unsupported needs-information workflow. Partner registration itself creates an unverified account; the review queue contains submitted verification dossiers, not every signup. No new SuperAdmin hierarchy, staff role elevation, production deployment, production migration or commit was performed. For UI/file inventory and manual checks see Dashboard `docs/handoffs/admin-users-verifications.md`.
