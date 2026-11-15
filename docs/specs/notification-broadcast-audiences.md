# Notification broadcast audiences

## Objective and acceptance
- `All`: active Student, Partner and Assessor accounts only.
- `Role`: exactly one canonical role name from Student, Partner, Assessor; reject staff roles and numeric enum strings.
- `Staff`: active Verifier, Moderator, Accountant and Admin accounts only.
- Dashboard offers these three audience choices. Historical Selected broadcasts remain supported by the API and history display; creating manually selected broadcasts is removed from this form.
- Preview, publish counts and worker delivery use the same eligibility filter. Existing published-at cutoff, expiry, authorization and deduplication remain in effect.

Assumption: system staff means the four internal roles Verifier, Moderator, Accountant, Admin; Assessor belongs to the user audience requested above.

## Plan and files
1. Add focused SQLite/xUnit regressions in `UniNet.Tests/NotificationsTests.cs` for exact recipients, preview/publish/worker agreement, inactive exclusions and invalid Role selections; reproduce failures.
2. Update `UniNet.Application/Services/NotificationBroadcastService.cs` validation and shared eligibility query. Fail closed for historical invalid Role audiences. Preserve Selected API compatibility; no schema migration.
3. Update Dashboard broadcast types and form: All/Role/Staff, three user roles, explanatory copy and history labels. Extend Chrome fixture checks.
4. Run backend notification tests, Dashboard build and browser fixture. Update handoff with results.

## Commands and conventions
- Backend: `dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationsTests --verbosity quiet -p:UseSharedCompilation=false -m:1 -nr:false` from UniNet_BE.
- Dashboard: `npm run build`; `node tests/notifications-browser-check.cjs` from UniNet_Dashboard (local Vite server on 5174).
- Follow existing C# service/query style and React controlled selects; example: `query.Where(a => a.Role == AccountRole.Student)`.
- Keep changes localized to broadcast targeting; do not alter individual notifications, permissions, money flows or deployed data. No new dependencies.

## Validation strategy
Exercise actual service create/preview/publish/process using all seven roles and an inactive account. Browser checks cover form options, role options, Staff payload and history label. Build validates TypeScript contracts.
