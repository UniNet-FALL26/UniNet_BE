# Users role JSON — 2026-11-02

## Storage and compatibility
- Users keeps common identity, images, bio, contact, verification and system columns. Role fields are stored as nullable JSONB `StudentJson` (UniversityName/Major/StudentCode), `PartnerJson` (OrganizationName/PartnerType/Industry/Website), and `AssessorJson` (the nine requested professional fields).
- EF8 owned `ToJson` mappings provide tracked updates and SQL JSON queries. Legacy student/partner domain properties are NotMapped adapters; existing request/response fields, website list encoding, registration, profile updates, portfolio, account search, verification and permissions stay compatible. Update SQL expressions through mapped JSON members, not the adapters.
- Student verification review still publishes the approved identity snapshot inside its transaction/account lock; JSON changes use tracked SaveChanges because EF8 cannot bulk-update owned JSON members.
- Existing assessor Major/UniversityName/OrganizationName data is copied to Expertise/University/CompanyName. Original legacy groups are also preserved, including data from prior roles. No professional values beyond existing data and neutral 0/[] defaults are invented.

## API and frontend
- `GET /api/profile/me` keeps existing flat properties and adds `profile.assessorJson` with camelCase keys. Student/partner API routes and payloads are unchanged.
- `PUT /api/profile/assessor` upserts common editable fields plus `assessorJson`; `DELETE /api/profile/assessor` clears only the professional JSON. Authentication and current active database role 2 are required; identity is the JWT subject. Neither endpoint can select another owner.
- DTO `AssessorProfileRequest`: fullName, nickname, avatarUrl, coverUrl, bio, contactEmail, phone, address, assessorJson. JSON keys: jobTitle, companyName, yearsOfExperience, expertise, education, university, linkedInUrl, gitHubUrl, portfolioUrl.
- Validate nonnegative integer experience (0–80), at most 30 nonblank expertise items (100 chars each), bounded text and HTTP(S) professional links. Managed portrait ownership checks remain in effect. Full update replaces professional fields; null clears the JSON.
- Existing assessor profile page displays all fields and includes an edit dialog with create/update and confirmed clear. Common fields/photos are preserved on clear, failed requests retain the draft, and stale responses are ignored when the account changes. Legacy major/university fallback applies only to old API responses missing assessorJson, so cleared data cannot reappear.

## Migration and rollout
- Migration: `20261102014809_UserRoleJson`. It adds JSON columns, backfills before dropping legacy columns, updates the PartnerType constraint and preserves the student university/major lookup as a PostgreSQL JSON expression index.
- Snapshot matches the model (`dotnet ef migrations has-pending-model-changes` reports no changes). The expression index is maintained explicitly in migration Up/Down.
- Deploy matching backend/schema together during a controlled restart: older backend binaries require the removed scalar columns. Existing FE student/partner contracts remain compatible; deploy the new assessor screen for professional editing.
- Back up the target database, inspect the generated migration SQL, then run against the intended target: `dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.Infrastructure`.
- Down restores student/partner scalar values before dropping JSON. It refuses to drop any nonnull AssessorJson because the old schema cannot store the new fields; export and explicitly clear those documents before an intentional downgrade. Do not bypass the guard without a backup.
- No migration was run on the configured application database. Verification used only a new local PostgreSQL cluster/schema; the temporary cluster has been stopped. No deployment/commit performed.

## Verification
- Full backend suite after storage changes: 661 passed, 28 skipped, four known baseline failures (three partner registration expectations and the skill-category constraint expectation). No new failures.
- Follow-up focused run including actual assessor HTTP CRUD and student/partner verification: 35 passed. New UserRoleJsonPostgresTests passed on real isolated PostgreSQL 18.6, including migration data preservation, JSON queries/CRUD, arrays and downgrade protection/restoration. It explicitly skips when UNINET_ROLE_JSON_TEST_POSTGRES is absent.
- Frontend `npx tsc --noEmit` passed. `node tests/assessor-profile-crud-browser-check.cjs` passed against localhost:8081: all nine fields, persistence, create/update/clear, save/clear failure retries, preserved identity, integer validation, and 320/390/768/1440px layouts. Screenshots under ignored `UniNet_FE/dist/assessor-profile-crud`.
- Unrelated existing notification changes in BE and other pending FE work were left outside this change.
