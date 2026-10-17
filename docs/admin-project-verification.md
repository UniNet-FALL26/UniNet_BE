# Admin project verification

All endpoints require an authenticated, active Admin account. Base route:
`/api/admin/project-verifications`.

| Method | Suffix | Purpose |
| --- | --- | --- |
| GET | (base) | Filter by status and inclusive submitted-at range (`from`, `to`), with `page`, `pageSize` |
| GET | `/pending-assignment` | Pending, unfinalized requests without an active or completed assignment |
| GET | `/overdue` | Pending, unfinalized requests with a missed scheduled meeting or overdue active assignment |
| GET | `/{verificationId}` | Evidence, reports, assignment history, payments and admin action history |
| GET | `/{verificationId}/reports` | All assignment reports in the verification, using each report's current content |
| GET | `/{verificationId}/refunds` | Refund history across linked payments |
| POST | `/{verificationId}/refunds` | Reserve a manual refund and audit its requesting admin |
| POST | `/{verificationId}/reviews` | Record a complaint review decision and reason |

List endpoints default to page 1, size 10 (maximum 100). Overdue assignment detection
uses `AssignmentDueAt`, `ReportDueAt`, or explicit Overdue status, excluding completed,
cancelled and replaced assignments. No arbitrary age deadline is inferred for unscheduled requests.

Refund body: `{"transactionId":"<guid>","amount":32000,"reason":"Authorized service refund"}`.
Only successful payments belonging to this verification qualify. Amount must be positive,
whole VND and within the payment balance after Pending and Success refunds.
Creation returns 201 and a Pending refund; it does not transfer money. Provider execution
and reconciliation must be handled by a payment module. Active Admin membership is the
current authorization boundary; no separate refund delegation policy exists in this project.

Review body: `{"decision":1,"reason":"Evidence was overlooked"}`
(`0` = Uphold, `1` = ReassessmentRequired).
A report must exist. Reviews are append-only decisions, visible in detail `actions`, and
do not edit report versions, reopen verification, change final results or trigger refunds.
Report details and version editing remain under ReportController.

Apply migration `AddVerificationAdminActions` before using these endpoints:

```powershell
dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.API
```
