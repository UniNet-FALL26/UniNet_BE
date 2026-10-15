# Verification payment API

## Configuration and deployment

Set server configuration through environment variables, user secrets or the ignored `.env` file:

```dotenv
PayOS__ClientId=<merchant client ID>
PayOS__ApiKey=<merchant API key>
PayOS__ChecksumKey=<merchant checksum key>
Payments__ReturnUrl=https://your-frontend.example/payment-return
Payments__CancelUrl=https://your-frontend.example/payment-cancel
Payments__LinkExpirationMinutes=30
Payments__AllowUserReconciliation=true
```

The provider also accepts `PAYOS_CLIENT_ID`, `PAYOS_API_KEY`, and `PAYOS_CHECKSUM_KEY`. Nested `PayOS__...` settings take precedence. Credentials and callback URLs must be supplied by the deployment; checkout returns `PAYMENT_NOT_CONFIGURED` (503) until configured. Callbacks are fixed server configuration, never supplied by clients. Merchant calls use the PayOS production HTTPS endpoint and a 30-second timeout without automatic POST retries or redirects.

Apply `20261014013750_AddPayOSPaymentReferences` before using these APIs. In addition to PayOS reference uniqueness, it includes verification model changes that were missing from the previous migration: package association, evidence submissions, AI review fields, cancellation fields, nullable finalizer, and the unique pending-verification index. It does not introduce a Payments table.

Historical verification packages are backfilled from their existing transaction package IDs when those agree. An unpaid historical request can be mapped only if exactly one active Student verification package exists. Ambiguous requests stop migration with an explicit error instead of receiving an arbitrary package. Resolve those mappings in a reviewed migration before deployment. Existing duplicate pending verifications or duplicate PayOS references also require review before the unique indexes can be applied. The migration was generated and its SQL inspected; it is not automatically applied by the API.

Register the public backend URL `https://your-backend.example/api/webhooks/payos` in the PayOS merchant channel. PayOS sends signed sample data during registration; valid unmatched events receive 200 without changing the ledger. Invalid signatures receive 400. No webhook is registered automatically at startup.

## Routes

All user routes require a Student JWT, an active account and ownership of the requested verification/project. Admin access is not implicitly granted. Webhooks use signature authentication and do not require JWT.

| Method | Route | Behavior |
| --- | --- | --- |
| POST | `/api/project-verifications/{verificationId}/payments` | No request body. Creates an attempt using the verification's package; returns 201 and `Location`. Reuses an unresolved Pending attempt instead of creating another payment link. |
| GET | `/api/project-verifications/{verificationId}/payments?page=1&pageSize=10` | Paginated history, including failed attempts. Page size 1–100; empty history returns 200. |
| GET | `/api/project-verifications/{verificationId}/payments/{transactionId}` | Reads the locally persisted state and verifies the transaction belongs to that verification. Does not query PayOS. |
| POST | `/api/payments/{transactionId}/reconcile` | Queries PayOS using the stored order code. No request body. Returns the updated transaction; disabled with `Payments__AllowUserReconciliation=false`. |
| POST | `/api/webhooks/payos` | Accepts the original PayOS JSON payload, verifies HMAC-SHA256 over sorted data and applies the event idempotently. Maximum body size 64 KiB. |

`TransactionResponse` contains `id`, `verificationId`, `packageId`, `amountVnd`, `currency`, `status`, provider/order/link identifiers, `checkoutUrl`, timestamps, `reconciliationRequired`, `requiresReview` and a safe `diagnosticCode`. Payment statuses use existing values: Pending=0, Success=1, Failed=2. No provider credentials or raw provider errors are returned.

## Business behavior

- Checkout requires a Pending, unfinalized verification for a Completed project. The selected package must remain active, be `PackageType.Verification`, target Student, and have a positive whole-VND price supported by PayOS. `Transaction.Amount` snapshots `Package.Price`; the frontend cannot set price, payer, package or order code.
- A transaction and `VerificationTransaction` association are committed before calling PayOS. Random order codes fit exactly within JavaScript numeric precision and are uniquely indexed for PayOS. One unresolved attempt is permitted by serializable database transactions across API instances.
- Network timeout, cancellation, malformed response, duplicate-order error, undocumented provider error and provider unavailability keep the attempt unresolved. POST checkout returns the same attempt. Reconcile it; do not offer a fresh checkout solely because local `expiredAt` passed. An unresolved attempt with no link can remain Pending until operators safely resolve it; provider “not found” is not treated as proof that an earlier in-flight create cannot finish.
- Explicit HTTP authentication/validation rejection with a structured provider error and no created-payment data can mark creation Failed. Provider-confirmed cancellation/expiry permits another attempt only when `amountPaid=0`. Partial or unknown paid amount keeps it Pending and may require operational review.
- A signed PayOS webhook or authenticated provider status is required for success. Order, amount, VND currency, payment-link ID, payer/package association and full payment (when amountPaid is available) are checked. Frontend return/cancel query parameters are never proof of payment.
- Repeated success does not change `paidAt`/`updatedAt` or create assignments, refunds or entitlements. Stale Pending/Failed results never downgrade Success. Verification and project final statuses are independent of payment.
- A real second charge is retained as Success with `DUPLICATE_PAYMENT_REVIEW_REQUIRED`, exposed as `requiresReview=true`; it is not accepted as an additional verification entitlement. Meeting eligibility excludes that marker. Operations must reconcile/refund such a charge through a separate approved refund workflow.
- Advertisement transactions share the ledger, but these routes and webhook handler process only transactions linked to a project verification. Existing advertisement records are left unchanged; no advertisement webhook behavior existed in this repository. A future advertisement payment handler must dispatch those events explicitly.
- PostgreSQL serializable transactions protect checkout and state updates. Concurrent write conflicts return `PAYMENT_CONFLICT` (409); retry the same operation. PayOS can redeliver webhook events. The provider HTTP call never holds a database transaction open.

Scheduling already requires a successful payment in `ProjectVerificationService`. Checkout does not require a meeting booking, since that would create a circular dependency. Assignment, deadline jobs, automatic reconciliation, refunds and webhook registration are separate capabilities outside these five endpoints.

## Verification

Tests cover package-price snapshots, invalid packages/roles/states, ownership and transaction association, timeout/retry, definitive rejection, duplicate/stale webhooks, missing-link recovery, partial payments, closure/retry, duplicate-charge review, shared advertisement ledger, and a second checkout while the first provider request is in flight. Provider tests use the official published signature vector, tampering checks, outbound signing, HTTP failure classification and status mapping. SQLite tests exercise application behavior; PostgreSQL serialization races require integration testing on a dedicated PostgreSQL test database.

Implementation validation: API build passed; 83 targeted payment/provider/HTTP/verification/database-model tests passed using an isolated test runner. The normal test project currently fails compilation in `PortfolioHttpTests.cs:192` because an existing `PartnerProfileRequest` call has 11 arguments while its constructor has 10. Payment changes do not modify that unrelated test. EF reported no pending model changes, and the new PostgreSQL migration SQL was generated and inspected. No migration was applied to the configured database, and no live PayOS charge was created. Real provider end-to-end testing and PostgreSQL race/migration execution remain deployment integration checks.

Official references: [PayOS merchant API](https://payos.vn/docs/api/), [payment-requests signature rules](https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/).
