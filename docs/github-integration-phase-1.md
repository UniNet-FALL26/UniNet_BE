# GitHub integration — Phase 1

Phase 1 adds GitHub OAuth connections, project-scoped Git Manager permissions, and repository discovery/link management. It does not synchronize Git activity, process webhooks, assign Git identities, or change Contribution calculations.

## Configuration

GitHub is disabled until all required settings are provided. Missing settings do not prevent the rest of the API from starting. A local setup can use `dotnet user-secrets set "GitHub:ClientId" "<client-id>" --project UniNet.API`, then set the matching `GitHub:ClientSecret`, callback URL, key-ring path, and return-target URLs the same way. Use deployment environment variables or a secret manager outside local development.

| Setting | Required | Notes |
|---|---:|---|
| `GitHub:ClientId` | Yes | OAuth App client ID. |
| `GitHub:ClientSecret` | Yes | Keep in a deployment secret store; never commit it. |
| `GitHub:CallbackUrl` | Yes | Exact HTTPS callback URL registered with the OAuth App. Loopback HTTP is accepted for local development. |
| `GitHub:KeyRingPath` | Yes | Existing, writable, persistent directory outside the database. All instances must share the same protected key ring and application name. |
| `GitHub:ReturnTargets:expo-web` | Yes | Allowlisted client return URL. Use a separate key/URL for each supported client. HTTPS is required except loopback HTTP in development. |
| `GitHub:ReturnTargets:expo-mobile` | Mobile clients | Allowlisted Expo/custom app deep link. The scheme must also be listed under `GitHub:MobileRedirectSchemes`; `exp` is the default for Expo Go. |
| `GitHub:MobileRedirectSchemes:0` | No | Defaults to `exp`. Add the exact custom URI scheme registered by a standalone mobile app. |
| `GitHub:Scopes:0` | No | Defaults to `repo`; required to support private repositories through an OAuth App. |
| `GitHub:Scopes:1` | No | Defaults to `offline_access` to request expiring tokens and refresh tokens. GitHub may not return refresh tokens in every configuration. |
| `GitHub:AuthorizationEndpoint` | No | Defaults to `https://github.com/login/oauth/authorize`; only the official `github.com` HTTPS host is accepted. |
| `GitHub:TokenEndpoint` | No | Defaults to `https://github.com/login/oauth/access_token`; only the official `github.com` HTTPS host is accepted. |
| `GitHub:ApiBaseUrl` | No | Defaults to `https://api.github.com/`; only the official `api.github.com` HTTPS host is accepted. |
| `GitHub:FlowLifetimeMinutes` | No | Defaults to 10; supported range is 1–30. |

For local development, store client credentials in User Secrets or the existing local `.env` mechanism rather than source-controlled settings. Create the key-ring directory before starting OAuth; point it to storage that survives process/container replacement, restrict filesystem access to the API service identity, and back it up according to deployment policy. Data Protection handles key rotation; production storage should also be protected by the host's encrypted persistent-volume or key-management controls. Do not copy key-ring contents into PostgreSQL.

The `repo` OAuth App scope grants broad repository read/write capability (and additional repository-related permissions) even though this backend only issues read requests. This is a GitHub OAuth App limitation for private-repository access, not a read-only credential. Do not use the received credential for write operations. `offline_access` is requested for refresh-token support. The API does not request email access.

If GitHub OAuth is disconnected in UniNet, the encrypted credentials are erased locally and linked repositories move to `PendingAuthorization`; repository associations and audit history remain. This is a local disconnect, not an automatic provider-wide OAuth App revocation. A user who wants to revoke the OAuth App grant at GitHub can do so in GitHub's authorized applications settings. A provider-side revocation or invalid token is detected on the next provider request and requires reauthorization. Reconnecting creates or updates the active UniNet-owned connection; it does not silently replace repository credentials.

## API

All IDs in project routes are UniNet project/profile/link IDs as indicated; `providerRepositoryId` is the stable numeric GitHub repository ID.

| Method and route | Authentication / permission | Purpose |
|---|---|---|
| `POST /api/github/oauth/start` | UniNet authenticated account | Body: `{ "returnTarget": "expo-web" }`. Creates an account-bound, single-use OAuth flow and returns `{ flowId, authorizationUrl, expiresAt }`. |
| `GET /api/github/oauth/callback` | Anonymous provider callback | Validates state, exchanges the code server-to-server, stores only encrypted pending credentials, then redirects to a configured target with `githubFlowId` and a non-secret `githubStatus`. |
| `POST /api/github/oauth/complete` | UniNet authenticated account | Body: `{ "flowId": "..." }`. Must be called by the account that started the flow. Returns connection metadata only. |
| `GET /api/github/connections` | UniNet authenticated account | Lists that account's GitHub connection metadata; never returns tokens. |
| `DELETE /api/github/connections/{connectionId}` | UniNet authenticated account | Locally disconnects only a connection owned by the caller. |
| `GET /api/projects/{projectId}/git/managers` | Project Creator | Lists active and revoked Git Manager grants. |
| `POST /api/projects/{projectId}/git/managers` | Project Creator | Body: `{ "userProfileId": "..." }`. Grants an active project member repository-management access. |
| `DELETE /api/projects/{projectId}/git/managers/{userProfileId}` | Project Creator | Revokes the active grant. |
| `GET /api/projects/{projectId}/git/repositories/discover?connectionId=...&page=1&pageSize=50` | Creator or active Git Manager; connection must belong to caller | Lists repositories readable by the selected OAuth connection. |
| `GET /api/projects/{projectId}/git/repositories?page=1&pageSize=50` | Creator or active project member | Lists only repositories linked to this project in stable name order. |
| `POST /api/projects/{projectId}/git/repositories` | Creator or active Git Manager | Body: `{ "providerRepositoryId": 123, "oauthConnectionId": "..." }`. Verifies current access before linking. |
| `GET /api/projects/{projectId}/git/repositories/{linkId}` | Creator or active project member | Reads one link in this project only. |
| `PUT /api/projects/{projectId}/git/repositories/{linkId}/credential` | Creator or active Git Manager; replacement connection must belong to caller | Body: `{ "oauthConnectionId": "..." }`. Verifies access before atomically replacing the selected credential and records history. |
| `DELETE /api/projects/{projectId}/git/repositories/{linkId}` | Creator or active Git Manager | Soft-unlinks the project association; shared repository metadata and history are retained. |

Errors use the existing `{ "code": "...", "message": "..." }` response shape. A normal project member cannot grant managers, discover repositories, link/unlink repositories, or replace credentials. `ProjectMember.Role` is not used as an authorization signal. The Creator is authorized by `Project.CreatorId` even when there is no corresponding membership row. A departing or kicked member's grants are revoked in the membership transaction and are not restored on rejoin.

The OAuth callback never requires a UniNet JWT. It accepts a high-entropy state whose hash is stored server-side with the initiating `Account.Id`, allowlisted return-target key, expiry, and one-time status. The authenticated completion request must match that account; a flow ID alone is not authorization. Provider codes and credentials are not placed in the client redirect URL. Expo Web should use an HTTPS allowlisted return target; Expo Go can use the configured `exp` deep link, and a standalone app must configure its registered custom scheme.

Pending encrypted OAuth credentials are cleared by a hosted cleanup worker within one hour after flow expiry. Completing or explicitly expiring a flow clears its pending credentials immediately; expired flow metadata is retained for audit and callback behavior.

## Persistence and data access

- `GitHubOAuthConnections` belongs to `Accounts` (`Account.Id`), not to a project. Multiple GitHub accounts can be connected to one UniNet account, and the same provider account may be connected to different UniNet accounts. A filtered unique index prevents duplicate active connections for the same UniNet/provider-account pair.
- Access and refresh tokens are protected by ASP.NET Core Data Protection. Only protected token strings are stored in the database, and API responses exclude them. Key-ring files must be persistent, shared between instances, access-controlled, and stored separately from the Git credential rows.
- `GitHubRepositories` deduplicates repository metadata by stable provider repository ID. `ProjectGitRepositories` is the explicit project-scoped association; project endpoints only return associations belonging to the requested project. Unlinking retains both association history and shared repository metadata.
- `ProjectGitRepositoryHistory` records link/unlink, credential replacement, authorization-pending, and metadata-change events. History rows are append-only in the DbContext.
- `ProjectGitManagerGrants` has historical grant rows and a filtered unique index for one active grant per project/profile. Active project membership is checked on every manager operation in addition to the grant.
- OAuth disconnect or the credential owner's project departure does not transfer a credential. The association remains present but moves to `PendingAuthorization` until an authorized manager explicitly verifies and replaces it. A member's Git Manager grant is revoked on leave/kick and is not restored on rejoin.

GitHub's authenticated repository response is checked for `permissions.pull` for private repositories. Public repositories are readable without that permission field. Organization SSO policy, revoked access, and provider-side repository changes may make a repository unavailable; the service does not silently substitute another member's OAuth token.

## Migration and validation

Migrations `20261104081807_AddGitHubRepositoryIntegration` and `20261104082437_AddProjectGitRepositoryConcurrency` are additive and have been generated. They have **not** been applied to any database. Do not run `database update` until the migrations are reviewed and separately approved.

Automated tests cover account-bound/single-use OAuth state, token-free connection responses, creator-only manager grants, grant invalidation on member departure, repository deduplication with project-scoped visibility, private repository read-access checks, and token refresh with protected token persistence. SQLite tests do not substitute for a PostgreSQL migration review; PostgreSQL filtered indexes, advisory-lock refresh serialization, and the generated SQL still require validation in an isolated database before rollout.

## Phase 2 — project Git identities

Phase 2 adds self-service GitHub identity linking for the project creator and active members. Linking requires a GitHub OAuth connection owned by the caller and a fresh authenticated GitHub account check. One profile may link multiple GitHub accounts; an account has at most one active owner within a project, while the same GitHub account can be linked in other projects.

| Method and route | Permission | Purpose |
|---|---|---|
| `GET /api/projects/{projectId}/git/identities` | Creator or active project member | Paginated identity list; optional `userProfileId` and `status` filters. |
| `POST /api/projects/{projectId}/git/identities` | Creator or active project member | Body: `{ "connectionId": "..." }`; verifies the caller-owned OAuth connection and links/reactivates that identity. |
| `GET /api/projects/{projectId}/git/identities/{identityId}` | Creator or active project member | Reads one identity scoped to the project. |
| `DELETE /api/projects/{projectId}/git/identities/{identityId}` | Identity owner, who must still be a project participant | Deactivates the identity without deleting it or its history. |
| `POST /api/projects/{projectId}/git/identities/{identityId}/reactivate` | Identity owner, who must still be a project participant | Body: `{ "connectionId": "..." }`; requires fresh proof for the same provider account and rejects an active conflicting owner. |
| `GET /api/projects/{projectId}/git/identities/{identityId}/history` | Creator or active project member | Returns paginated append-only identity lifecycle history. |

Leaving or being kicked deactivates every active identity in the existing membership transaction, appends the corresponding history event, and does not revoke the member's UniNet-owned OAuth connection. A former member cannot reactivate an identity until they again qualify as a project participant. Ownership is never transferred by editing an existing identity; after unlinking, another member may link the same provider account, creating a separate identity history. Historical identity records are retained.

The generated `20261104150025_AddProjectGitMemberIdentities` migration creates only `ProjectGitMemberIdentities` and `ProjectGitMemberIdentityHistory`, including a filtered unique index for active project/provider/account ownership and restrictive foreign keys. It has **not** been applied. Git synchronization, Contribution scoring, and PostgreSQL migration execution remain out of scope for this phase.
