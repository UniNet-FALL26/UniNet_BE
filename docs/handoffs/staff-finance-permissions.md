# Staff roles and manual finance

## Contract
- Roles: Student0, Partner1, Assessor2, Verifier3, Moderator4, Accountant5, Admin6. The former Verificator is now Assessor (expert appointments/reports); Verifier handles Student/Partner identity reviews.
- Permission policy enforces role scope even if an old override explicitly grants a foreign permission. Admin can revoke/restore scoped staff permissions. Staff lists/details require staff.manage; self elevation and staff account locking remain prohibited.
- Admin and Accountant get the eight finance.* permissions. Every mutation checks permission at the backend. Public registration remains Student/Partner only.

## Financial behavior
- Append-only WalletEntries: documented whole-VND credits, normalized owner-scoped unique reference; immutable amount/reason/actor. No checkout-to-wallet credit.
- Available balance = ledger total minus Requested/Approved/Ordered reservations. Durable request keys reject changed payloads, including after cancellation. Student/Partner owner APIs return only their data; internal paying-account/staff audit details stay in administrative DTOs.
- Requested -> Approved -> Ordered -> Paid. Requested/Approved may be rejected; owner cancels only Requested. Rejection preserves the original approval audit. Ordered stays reserved while transfer is uncertain; no ordinary reject/cancel after ordering.
- One order per withdrawal. Manual transfer happens outside the app. Confirmation records bank-account/reference, atomically posts one debit and marks Paid. Matching retries are idempotent. Serialization/unique keys prevent duplicate financial effects; bank evidence cannot confirm both a refund and a withdrawal.
- New staff refunds (including legacy Admin endpoint) require finance.refund.create and another authorized reviewer. Approval-pending reserves payment balance and is blocked by both refund worker selection and direct ProcessAsync. Shared reservation rules retain failed timeout refunds. Existing automatic/legacy rows retain explicit approval exemption. Manual confirmation requires bank evidence and an undispatched approved refund; provider timeouts are never treated as successful transfers.
- /api/admin/finance: summary, paginated payments/refunds/withdrawals/ledger, income-recipient search, credits, refund review/confirmation, payout order/confirmation and aggregate CSV report. /api/wallet: owner balance/latest100 entries/withdrawals, request and cancel.
- Decimal money travels as JSON strings; state/page/count stay numbers. Clients submit decimal text and use exact BigInt formatting/arithmetic. Sensitive responses are no-store.

## Migration and rollout
Apply both migrations together: 20261101140846_StaffRolesAndFinance and 20261101142007_PreserveWithdrawalRejectionAudit. The first remaps old staff values atomically, changes the single-Admin index, revokes sessions and remaps persisted notification audiences. Staff must log in again. Downgrade refuses new-role accounts or financial history; archive through an explicit reviewed migration instead of dropping financial records.

Production was not changed. Dashboard .env currently targets Render and backend .env targets hosted Neon. Local UI changes alone cannot add these endpoints to that server. Deploy the matching backend and reviewed migrations before enabling live operations; no real funds or balances were modified during development. Scratch PostgreSQL at localhost:55439 was used only for isolated-schema tests.

## Validation
- Focused backend: 175 passed; final financial HTTP/service/role/staff checks after owner-DTO privacy tightening: 16 passed (finance-http.trx).
- Real PostgreSQL: migration up/down and session invalidation, concurrent overdraw prevention, concurrent refund-vs-withdrawal duplicate bank evidence: 3 passed.
- Dashboard browser fixtures: Admin/Accountant/Verifier/Moderator/Assessor; actual approval/order/confirmation, exact large refund amount, self-review exclusion, documented credit, CSV download and role denial passed. Desktop/mobile screenshots in ignored dist/finance-check.
- End-user browser fixtures: Student/Partner wallet, durable retry after 503, reservation/cancel and Settings entry passed. FE typecheck/export and Dashboard build pass.
- Full regression: 609 passed, 28 skipped, 4 failed. The role-literal HTTP failure was fixed. Four unrelated existing failures remain in AuthTests partner-name expectations (3) and DatabaseDesignTests skill-category constraint (1); their relevant production logic was not changed by this feature.
- Fresh-context adversarial implementation review: no remaining P1/P2 findings after permission, audit-history and exact-money corrections.

Reviewable idempotent SQL: docs/migrations/staff-roles-finance.sql (generated from the last pre-feature migration through both new migrations). Staff creation of all four canonical non-Admin staff roles, and denial of their ability to create further staff, also passed a focused Auth test. Temporary PostgreSQL cluster was stopped and removed after checks.

No dependencies, commits or deployment changes added.
