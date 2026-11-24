# Phase 3 E2E local setup

This profile starts the existing UniNet API against a dedicated local GitHub Integration E2E configuration. It does not apply migrations, contact Neon/shared databases, or call GitHub from the checker.

## Files

- `.env.e2e.example`: placeholder-only template; safe to keep in source control.
- `.env.e2e`: local file to fill. It is explicitly Git-ignored.
- `scripts/check-phase3-e2e.ps1`: checks syntax, placeholders, local database target/port, callback/webhook URLs, key-ring path and worker flags. It performs only a TCP probe of PostgreSQL and checks Docker metadata; it does not connect to the database or GitHub.
- `scripts/start-phase3-e2e.ps1`: repeats the checks and starts the API with a clean child-process environment. It never runs migrations.
- `scripts/phase3-e2e-common.ps1`: shared parser and validation functions.

The connection string is locked to `127.0.0.1:55432`, database `uninet_e2e`, username `uninet_test`. The local Docker engine currently reports container `uninet-postgres-e2e`, image `postgres:17`, mapped to that port. This setup did not authenticate to the DB or verify its schema. The user reports Phase 1–3 migrations were applied to the DB configured at the time; confirm that it was this exact local target before enabling sync. Never substitute Neon/shared config.

## Fill `.env.e2e`

Open `.env.e2e` and fill only the placeholders:

| Keys | Source/requirement |
|---|---|
| `ConnectionStrings__UniNet` password | The password set for the local `uninet_test` PostgreSQL user. Keep host, port, database and username fixed. If the password contains semicolons, quote the password value according to Npgsql connection-string rules. The `.env` parser preserves further `=` characters and removes only a matching outer quote pair around the whole value. |
| `Jwt__Key` | A fresh random local signing key of at least 32 UTF-8 bytes. Generate/store it locally; do not reuse a Development/production key. |
| `GitHub__ClientId`, `GitHub__ClientSecret` | The test OAuth App under GitHub **Settings → Developer settings → OAuth Apps**. Do not copy credentials from another environment. |
| `GitHubWebhook__Secret` | A fresh random webhook secret. Enter the same value in the test repository's Webhook settings. |
| `GitHubE2E__WebhookPublicUrl` | The HTTPS URL from the test ngrok tunnel, followed by `/api/webhooks/github`. |

The keys were checked against source. `GitHub` is the OAuth section name; `GitHubIntegration` is not. Configure callback and return target together using either `http://localhost:5239/api/github/oauth/callback` and `http://localhost:5239/swagger/index.html`, or the same HTTPS ngrok origin as `GitHubE2E__WebhookPublicUrl` with paths `/api/github/oauth/callback` and `/swagger/index.html`. The webhook origin is the explicit public origin allowlist for this E2E checker; it must use an ngrok host and HTTPS. Credentials, query strings and fragments are refused. Configure the matching callback in the test OAuth App. The configured scopes default to `repo offline_access`; no GitHub write/admin scope is added for synchronization. Repository webhook registration is a separate GitHub repository-owner action.

The profile sets `ASPNETCORE_ENVIRONMENT=E2E`, `ASPNETCORE_URLS=http://localhost:5239`, and `ASPNETCORE_CONTENTROOT` to `UniNet.API`. It disables local `.env` loading and Development User Secrets for the child process. No `appsettings.json` or `launchSettings.json` edits are needed. The local Data Protection key ring is persisted under the Git-ignored `UniNet.API/.phase3-e2e-data-protection/` directory.

`GitHubSync__Enabled=true` is present so sync works after prechecks. The checker requires the connection string to match the allowlist, a reachable local PostgreSQL port, filled OAuth/webhook values and a free API port before the launcher can start. It does not verify the DB migration history or validate OAuth secrets by making network calls. Confirm the DB schema and test OAuth App setup first. The scripts do not migrate automatically.

## Check and start

Stop any API already using port 5239 with Ctrl+C in its owning terminal; the launcher will not stop it or replace its files. The launcher uses `dotnet run --no-restore` so it does not contact package feeds during E2E startup. Ensure NuGet assets already exist; if `UniNet.API/obj/project.assets.json` is missing, run the repository's normal `dotnet restore`/build first.

Then, from the repository root, run:

```powershell
.\scripts\check-phase3-e2e.ps1
```

If it reports only PASS/WARNING and exits successfully, start the isolated backend:

```powershell
.\scripts\start-phase3-e2e.ps1
```

The checker prints status and key names only; it never prints secret values or the connection string. It checks the local PostgreSQL TCP port and, when Docker is available, whether a container publishes that port. It does not read/write the database or call GitHub. The launcher passes credentials in the child process environment, not command-line arguments, and clears inherited application configuration so local `.env`, User Secrets, Neon, Cloudinary, payment and other host values cannot bleed into E2E.

The current `.env.e2e` contains placeholders, and port 5239 is already occupied by a running `UniNet.API` process. The checker currently fails on those conditions; no backend was started by this setup task.

## OAuth and API flow

1. In the test OAuth App, set its callback URL to exactly the configured `GitHub__CallbackUrl`, using either the localhost or HTTPS ngrok option above.
2. Create/use a dedicated test UniNet Student account in `uninet_e2e`; do not use production account credentials.
3. Use `POST /api/auth/login` to obtain a test bearer token (or register a local Student with `POST /api/auth/register/student`).
4. In Swagger, authorize with that bearer token. Call `POST /api/github/oauth/start` with `{"returnTarget":"phase3-e2e"}` and open the returned GitHub authorization URL.
5. After GitHub redirects to Swagger with `githubFlowId` and `githubStatus=ready`, call `POST /api/github/oauth/complete` with the flow ID and bearer token.
6. As the Project Creator, discover/link the test repository. Create a project member and link that member's Git Identity before running the E2E scenarios.

## Webhook/ngrok

In another terminal, run:

```powershell
ngrok http 5239
```

Put its HTTPS forwarding URL plus `/api/webhooks/github` in `GitHubE2E__WebhookPublicUrl`. Then in the test repository open **Settings → Webhooks → Add webhook**:

- Payload URL: the ngrok URL.
- Content type: `application/json`.
- Secret: the same value as `GitHubWebhook__Secret`.
- Keep SSL verification enabled and choose individual events: Pushes, Pull requests, Issues, Issue comments, Pull request reviews, Pull request review comments, and Releases.

Check **Recent Deliveries** after saving. The webhook payload is only a trigger; sync reads history through GitHub's API using the linked credential. Scheduled Sync is the fallback for missed webhook events. No production domain is created or configured by these scripts.

## Worker safety

Only in the explicit `E2E` environment, `GitHubE2E:DisableUnrelatedWorkers=true` prevents Notification, Verification/payment, expired-token, AccountSessionConnections polling and PortraitCleanup hosted workers from starting. `VerificationWorker:Enabled=false` is also set. This flag does not change default Development/Production behavior. `GitHubOAuthFlowCleanupWorker` and `GitSyncWorker` remain registered; the latter runs only because `GitHubSync:Enabled=true` in the isolated E2E environment.

## Stop and return to Development

- Stop the API and ngrok in their terminals with Ctrl+C.
- Environment variables are passed only to the child process; the launcher does not mutate the calling shell's variables.
- The `.env.e2e` file and Data Protection keys remain local/ignored.
- Resume normal development with the existing `http` launch profile or usual `dotnet run` command. The E2E scripts do not edit launch settings or appsettings.

## Common failures

- `ConnectionStrings__UniNet`: only the password is variable; other target fields are fixed and validated.
- PostgreSQL port 55432 unreachable: start the dedicated local test database.
- API port 5239 occupied: stop the existing backend in its owning terminal, then rerun the checker.
- Callback URL failure: align the test OAuth App callback and `GitHub__CallbackUrl` exactly.
- Webhook URL failure: use the current ngrok HTTPS hostname and route.
- Parser error: use one `KEY=value` per line, no duplicate names, and quote values whose outer whitespace/quotes must be preserved.
- Git Sync must remain disabled outside this E2E environment; the launcher refuses placeholder secrets and non-local database targets.
