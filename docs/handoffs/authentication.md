# Authentication handoff

Database/DTO update: see [database v2](database-v2.md). `UserProfile` now maps to `Users`; names are FullName/Nickname/OrganizationName, and TaxCode lives only in verification snapshots. This document's original schema/contract notes below describe v1.

## Schema and contracts

`Account` owns email, password hash, Google identity, role, status, verification and login metadata. `UserProfile` is shared by Student and Partner, with a unique `AccountId`. `RefreshToken` stores SHA-256 hashes of random 64 byte tokens. `UniNet.Domain/Enums` is the one backend source for `AccountRole`, `AccountStatus` and `PartnerType`. The `AddAuthenticationAndUserProfiles` EF migration creates all three tables, two FKs and unique indexes on account email, Google ID, profile account ID and refresh token hash. No existing tables are dropped by `Up`.

## Endpoints

| Method | Route | Authorization | Purpose |
| --- | --- | --- | --- |
| POST | `/api/auth/register/student` | Public | Create Student account and basic profile |
| POST | `/api/auth/register/partner` | Public | Create Partner account and basic profile |
| POST | `/api/auth/login` | Public | Password login for all roles |
| POST | `/api/auth/google` | Public | Verify Google ID token and find, link or create account |
| POST | `/api/auth/refresh` | Public | Rotate refresh token |
| POST | `/api/auth/logout` | Bearer | Revoke supplied refresh token owned by caller |
| POST | `/api/auth/logout-all` | Bearer | Revoke caller's active refresh tokens |
| GET | `/api/auth/me` | Bearer | Current account and profile |
| GET | `/api/profile/me` | Bearer | Current account and profile |
| PUT | `/api/profile/student` | Student | Update own shared profile |
| PUT | `/api/profile/partner` | Partner | Update own shared profile |

Registration saves account and profile in one EF `SaveChanges`. Access JWTs last 15 to 60 minutes (30 by default). Refresh tokens last 30 days, rotate once using a conditional database update, and old expired or revoked records are cleaned daily after seven days. Profile updates use the authenticated account ID from the JWT. Responses contain DTOs and no password or token hashes. Errors have `{code,message}`.

## Verification and configuration

`dotnet test` covers Student and Partner registration, duplicate email, weak password, login and status rules, profile role isolation, refresh rotation and logout. `dotnet build` and frontend `npx tsc --noEmit` pass. The migration was applied to the supplied empty Neon schema on 2026-09-23 using EF-generated SQL through `psql` with TLS and channel binding. This was necessary because local Windows Npgsql failed the TLS handshake; the generated script was removed afterward. VerifyFull also requires a suitable root certificate for the local `psql` installation.

Google client IDs have not yet been provided. Configure `Google__ClientIds__0` and the mobile app's OAuth client IDs before enabling Google buttons. A trusted mail delivery service is also required to make the password reset illustration functional; it is currently a clearly labeled UI placeholder.

## Project layout

The solution uses `UniNet.Application` for auth use cases and `UniNet.Infrastructure` for EF Core data access. `UniNet.Infrastructure/Data/Migrations` contains the original migration ID and model snapshot. The project rename did not create a new migration or alter the database schema. `UniNet.Application` still references `UniNet.Infrastructure` as it did before the rename; changing that dependency requires a separate architectural change.
