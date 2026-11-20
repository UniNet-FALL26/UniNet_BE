# UNINET — REAL ENV AUTO-CONFIGURATION

Date: 2026-11-20. Source HEAD `213e648c039b7bb2d4d9b9437f914a7a771e3763`, with all existing uncommitted runtime-audit changes retained.

## Result

**PARTIAL — MISSING PUBLIC URL OR PROVIDER VALIDATION.**

The real ignored root environment file was updated with two source scope settings after proving they exactly match the existing runtime policy. Its original 21 values, including Neon/JWT, all worker flags and other integrations, remain unchanged.

**App/OAuth credential transfer is held pending confirmation of intended shared use.** Authenticated read-only provider verification proves the existing PEM matches the App configured in E2E. However, the App's name/slug suggests dedicated E2E/test use, the provider webhook is registered to the E2E destination, and the currently active ngrok tunnel targets the protected E2E backend on port5239. The real environment has no App identity that establishes intended equivalence. Naming alone is not proof of an isolation policy, but neither is it authorization to move a dedicated App's credentials to a shared Neon runtime.

The task explicitly says not to transfer a separate E2E App's credentials before confirming its intended use. A question was sent asking whether this exact App and separate OAuth credential pair are approved for Neon, with App/workers still disabled and provider registrations unchanged. No answer had been received at handoff. No secret needs to be resent: the source material is present and validated as described below.

A separate question asks for the actual public HTTPS origin of the Neon-backed backend. The E2E origin cannot be substituted for it. Approved frontend return-target URLs/names also remain necessary; an existing CORS origin is not automatically an OAuth return-target policy.

## 1. Sources, backup and preservation

Reviewed the current root configuration, GitHub App E2E source, reference template, JSON/default options, [loader](../../UniNet.API/Services/LocalEnvConfiguration.cs), [Program](../../UniNet.API/Program.cs), [worker registration](../../UniNet.API/Services/RuntimeWorkerRegistration.cs), [deployment validator](../../UniNet.API/Services/GitHubDeploymentReadiness.cs), E2E launcher and current OAuth/App/webhook routes/services. Backend instructions were reviewed before changes.

Root `.env` remains Git-ignored/untracked. A byte-for-byte original was backed up outside the repository in `%LOCALAPPDATA%/UniNetRealEnvAutoConfiguration/20261120-113208/env.original`; the directory is restricted to the current Windows user and SYSTEM. Source-file SHA-256 is retained privately to verify that `.env.github-app.e2e` was not edited. No secret, actual endpoint value, private-key path/content or complete connection string is included in this report.

The source stores a **file path** under its launcher-only `GitHubAppE2E:PrivateKeyPath`. Its existing launcher reads that file and injects `GitHubApp:PrivateKeyPem`. The path itself must never be used as the PEM value. The private file is outside the repository and was not copied, rewritten or rotated.

## 2. Transfer map

| Keys / group | Status | Actual action |
|---|---|---|
| `GitHub:Scopes:0`, `GitHub:Scopes:1` | COPIED | Persisted in the root OAuth section. Exact equality with current code defaults was checked first; no effective scope/policy change. |
| All21 original root assignments | PRESERVED | Parsed values unchanged, including database, JWT, merchant/storage/AI/CORS values and eight disabled flags. |
| Source `GitHubAppE2E:PrivateKeyPath` → runtime `GitHubApp:PrivateKeyPath` | TRANSFORMED | Adapter implemented/tested and existing source PEM validated; **runtime path not populated until intended App reuse is confirmed**. |
| `GitHub:ClientId`, `GitHub:ClientSecret` | BLOCKED | Available in source; intended runtime reuse of this separate OAuth App still needs confirmation. The OAuth Client ID differs from the GitHub App Client ID. |
| `GitHubApp:AppId`, `ClientId`, `ClientSecret`, `WebhookSecret` | BLOCKED | Source identity/key verified, but shared-runtime intent not established. No credential silently transferred. |
| `GitHubWebhook:Secret` | BLOCKED | Source exists; no blind assumption that an E2E registration secret is the runtime registration secret. |
| `GitHub:KeyRingPath`, `KeyRingCertificatePath`, `KeyRingCertificatePassword` | REUSED_AFTER_VALIDATION / BLOCKED for transfer | Existing source directory/certificate and encryption roundtrip verified without generating keys. No root key-ring selection changed before shared-use confirmation. |
| OAuth/App callbacks and `ReturnTargets:e2e` | ENVIRONMENT_SPECIFIC | Not copied. The source URLs are associated with the E2E tunnel. |
| `GitHubAppE2E:WebhookPublicUrl` | ENVIRONMENT_SPECIFIC | Launcher/provider-registration metadata, not an invented Backend runtime key. Not copied. |
| Source DB connection, local ports and environment | ENVIRONMENT_SPECIFIC | Not copied. Existing Neon target preserved exactly. |
| Source JWT signing key/issuer/audience | PRESERVED | Runtime JWT configuration preserved; no source test JWT values imported. |
| E2E isolation/skip-file flags and worker-enabled values | ENVIRONMENT_SPECIFIC | Not copied. Root App/global/manual/scoped/background flags remain false. |
| Other tuning values already supplied by current source defaults | NOT_REQUIRED | No unnecessary aliases/default overrides added. |

This is an actual partial root-file update, not a claim that blocked credentials were copied. Do not interpret source-only validation or a prepared adapter as a completed runtime credential transfer.

## 3. Provider and cryptographic evidence

| Verification | Result and scope |
|---|---|
| Existing PEM parse / private RSA / minimum2048 bits | REUSED_AFTER_VALIDATION |
| PEM location outside repository | REUSED_AFTER_VALIDATION |
| Authenticated `GET https://api.github.com/app` | HTTP200; returned App ID and Client ID match source configuration |
| Source App name/slug | E2E/test naming detected; intended Neon use remains unconfirmed |
| Authenticated App webhook-config GET | HTTP200; provider URL matches E2E configuration, not an established Neon endpoint |
| OAuth vs App identity | Different credential pairs; do not treat a successful App JWT request as validation of the OAuth Client Secret |
| Client-secret validity at provider | Not independently established; no OAuth exchange/token check was performed |
| Existing key-ring directory / certificate | Present; certificate private key and validity dates pass offline checks |
| Existing token-protection purpose/application name | Offline protect/unprotect probe succeeds using existing keys; automatic key generation disabled |
| Key-ring file fingerprints before/after probe | Unchanged; no key generation/rotation |
| Source-only transformed deployment configuration | Strict offline deployment validation passes with source E2E HTTPS URLs and the validated private file; **not** proof that those URLs are suitable for Neon |
| ngrok tunnel inspection / public health GET | Source origin is active, public health200, and local tunnel maps to protected E2E port5239; it is not the Neon runtime origin |

Provider checks used short-lived App authentication JWTs for authenticated **GET** requests only. They did not create an OAuth/user/installation access token, register/change a webhook, modify App permissions, request a new installation token, sync repositories or use an E2E user session. Provider response bodies were not persisted; only sanitized validation results were recorded.

The encryption probe is a local constant string, not an OAuth token or business record. It uses the existing application discriminator and protector purpose, with automatic key generation disabled. It does not prove compatibility with arbitrary ciphertext encrypted by some other deployment. Read-only Neon inspection currently found no existing Git OAuth/App user-credential rows, so no historical Neon Git token was migrated or decrypted.

## 4. Minimal private-key adapter

Added [GitHubPrivateKeyConfiguration](../../UniNet.API/Services/GitHubPrivateKeyConfiguration.cs), called after environment/CLI precedence is established in [LocalEnvConfiguration](../../UniNet.API/Services/LocalEnvConfiguration.cs):

1. An existing nonempty `GitHubApp:PrivateKeyPem` always wins. Existing E2E secure-process injection remains unchanged.
2. Otherwise an explicitly configured `GitHubApp:PrivateKeyPath` is read; relative paths resolve against API content root.
3. Only a bounded, nonempty file is read; its PEM is parsed as an RSA private key of at least2048 bits.
4. PEM is assigned to in-memory configuration for existing options/provider consumers. No multiline PEM or temporary plaintext key is written into the repository/environment file.
5. File/format errors produce a fixed startup message without the path, PEM or inner exception.
6. No Base64 or literal backslash-n conversion is guessed. Those formats are not needed for the actual source file.

Updated [the environment reference](../../.env.example) with the optional file input and explicit PEM precedence. No fake credential or private path is included.

Seven new [adapter cases](../../UniNet.Tests/GitHubPrivateKeyConfigurationTests.cs) cover absent optional input, injected-PEM precedence, absolute/relative file loading/binding, unchanged file bytes, and empty/corrupt/oversized files with redacted errors. An initial test exposed that `InvalidDataException` was not covered by the generic IO catch; it was added to the adapter's redaction boundary and both affected cases passed in the final run. No existing assertion or validation was relaxed.

## 5. Runtime validation and safety

- Backend build: PASS, zero errors, existing warnings only.
- Final focused configuration/OAuth-App/webhook/JWT/worker regression: **61 PASS /0 FAIL /0 SKIP**, including all7 new adapter cases. The earlier43 PASS/2 FAIL adapter run is superseded by these same corrected cases, not added to totals.
- Temporary newly built Development host: PID40108, ephemeral port62263. Seven read-only HTTP checks PASS, then the process was stopped.
- Health200; broad GitHub readiness503 as expected while disabled; instance/queue200 with the actual `Degraded` body asserted.
- Unauthenticated auth-me, Git connections and Official workflow reads return401. No private evidence/account data was fetched.
- The temporary host's database connection retained the same Neon identity/TLS and additionally enforced read-only transaction defaults. No protected E2E process was stopped/replaced.
- Root background/global/manual/scoped/App/Verification/Assessor flags remain false. No producer, consumer, maintenance loop or webhook registration was activated. Protected E2E still reports PID40756/health200.
- Original Neon connection and JWT values unchanged. Read-only Neon identity/history query:104 history rows, all103 source IDs present, zero source pending, unchanged extra `20261119081037_AddMessagingAndPush`; model snapshot consistent. No history reconciliation or migration was attempted.
- Other integrations and Contribution V2 policies/lifecycle remain unchanged. No sync/job, peer rating, snapshot, run, finalization or publication was created.

Protected receipts include before/source hashes, key comparison, sanitized provider validation, source-only offline validation, read-only Neon result, root scope-transfer result, TRX, startup statuses and backup. No full connection string, private PEM, password or token is in this document.

## 6. Exact remaining information

| Missing runtime input | Why not copied / how to resolve | Startup requirement |
|---|---|---|
| Intended GitHub App/OAuth reuse confirmation | Root has no existing provider identity; source App is E2E/test-named with E2E registrations. Confirm that these exact source credential pairs are approved for shared Neon use. No secret needs to be resent. | Only GitHub feature activation/credential transfer |
| `GitHub__CallbackUrl` | Need verified Neon-backed HTTPS origin plus `/api/github/oauth/callback`; source tunnel is a different backend. Obtain from the actual hosted/runtime configuration and verify provider registration. | OAuth feature, not base startup |
| `GitHubApp__CallbackUrl` | Need verified Neon-backed HTTPS origin plus `/api/github/app/authorization/callback`, with provider-side registration compatibility. | App feature, not disabled base startup |
| `GitHub__ReturnTargets__<approved-name>` / `GitHubApp__ReturnTargets__<approved-name>` | Need approved frontend/deep-link destination and client-agreed target name. Source `e2e` target belongs to test flow; do not infer from CORS alone. | Corresponding feature |
| Public webhook registration target | Existing routes are `/api/webhooks/github` and `/api/webhooks/github/app`; GitHub currently points the App webhook to E2E. A real origin and separately authorized registration plan are required. There is no runtime `WebhookPublicUrl` option to invent. | Live webhook use |
| Client-secret provider validation | Sources exist, but validating private key/App identity does not validate both OAuth/App Client Secrets. Use authorized provider configuration/relevant future authorized OAuth flow; no token was created here. | Feature readiness assertion |
| Worker ownership | Identify every backend/Render/consumer before enabling any persistent background processing. Keep all current root flags false. | Worker activation only |

## 7. Handoff

No need to repeat PEM/provider GET/key-ring format discovery or invent new credentials. Once intended reuse is explicitly confirmed, recheck root/source hashes and any intervening legitimate edits, then supplement only the missing approved credential/key-source fields. Keep public callbacks/return targets blocked until their runtime destination is verified. Do not edit E2E provider registrations, change the live tunnel, enable App/workers or copy source DB/JWT values as part of that transfer.

Current-task changes: root scope assignments; private-key adapter and its loader call;7 adapter tests; environment-reference notes; this report and NEXT_AGENT. All earlier uncommitted worker-registration/audit work was preserved. No migration/business logic change or commit/push.
