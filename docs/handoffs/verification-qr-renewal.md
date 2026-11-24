# Verification QR renewal

- POST /api/project-verifications/{verificationId}/payments/renew, Student JWT,
  no body. Returns TransactionResponse; existing payment contracts unchanged.
- VerificationPaymentRenewalService composes owned transaction operations and
  existing verification scheduling. Registered scoped, injected at renew action.
- Paid/review attempts return existing state. Live pending attempt returns as-is.
  Expired pending attempts must reconcile with provider and be closed with zero
  money before reservation/new payment. Unresolved/partial states stop renewal.
- Re-fetch detail, re-hold original interval under existing policy/capacity/
  ownership/assignment/payment checks, then call guarded CreateAsync. New hold
  token/deadline comes from server; repeating a live hold does not extend it.
- Missing/past/unavailable slot returns conflict; frontend offers schedule change.
  Unknown provider state is never guessed or converted to Failed by local time.
- Tests: 45 TransactionTests/TransactionHttpTests pass, covering fresh renewal,
  idempotent repeats, owner/role scope, closed/unpaid prerequisite, paid/partial/
  pending protection and unavailable capacity. No provider traffic in tests.
- Run dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore
  --disable-build-servers -m:1 /p:UseSharedCompilation=false
  --filter "FullyQualifiedName~TransactionTests|FullyQualifiedName~TransactionHttpTests".
- No database migration or new dependency. Updated API process required.
