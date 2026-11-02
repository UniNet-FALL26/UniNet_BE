# Assessor wallet eligibility — 2026-11-01

Requested alongside FE Settings changes: Income Wallet is now for Assessor rather than Student/Partner. `WalletController` requires authentication; `FinanceService.Owner` checks the current database role and requires an active Assessor on every owner operation. Credit recipient search in `FinanceRefunds` uses the same eligibility. Existing ownership checks, accountant/admin permission checks, money validation, idempotency, balances, holds and stored financial records are preserved. No schema migration or automatic data changes.

`FinanceWalletTests` passes 15 tests, including new role-denial, active/suspended Assessor, recipient lookup and controller metadata regressions. Existing wallet finance fixtures now use Assessor. `FinancePostgresTests` fixture owners updated but PostgreSQL runtime tests were not executed.

Command: `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --filter FullyQualifiedName~FinanceWalletTests --verbosity quiet -p:UseSharedCompilation=false -m:1 -nr:false`.

FE TypeScript/export, role-entry tests and isolated mocked wallet/browser checks also passed. No real credits, withdrawals or deployments. Deploy matching FE/BE changes together; existing Student/Partner data is retained even though their wallet owner endpoints are now denied.

## Wallet access correction — 2026-11-02

- Reproduced HTTP 403 for an active database Assessor when its signed token carries `2` or an earlier `Student` role. A canonical `Assessor` token already worked; the user's actual browser session was unavailable for inspection.
- Owner endpoints now delegate role authorization to the existing database check instead of additionally requiring a cached `Assessor` claim. Identity remains the authenticated JWT subject; an `accountId` query cannot select another wallet. Other database roles are denied even with an `Assessor` claim, and changing the database role immediately removes wallet access.
- Finance admin endpoints retain `Admin,Accountant` authorization and database permission checks. Staff manage user withdrawals through `/api/admin/finance`, not the personal wallet endpoint.
- Updated the older finance HTTP fixture to use Assessor owners. Added real HTTP regressions for token role mismatch, all non-owner roles, anonymous requests, cross-owner cancellation, query tampering, and finance access. Final full-suite run: all 21 wallet/role tests passed; 622 passed overall, 28 skipped, 4 failed outside wallet code (three partner registration assertions and the skill-category check-constraint assertion). Build succeeded. No real financial data changed; restart/redeploy the backend to apply the controller change.
