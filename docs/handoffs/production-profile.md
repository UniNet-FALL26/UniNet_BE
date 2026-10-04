# Production profile API

- Existing CareerProfiles UNIQUE(UserId), AppearanceJson and Skills schema reused. Configured PostgreSQL has no pending migrations; no migration added.
- Added owner-only `PUT /api/profile/appearance/me`, active `GET /api/profile/skills`, anonymous public portfolio read with active-account/IsPublic guard. Appearance save preserves content/privacy and unknown root metadata; content save cannot replace appearance.
- SkillsJson stores SkillId/Level/YearsOfExperience references. Responses resolve catalog metadata; legacy names are accepted only when they match the catalog. Added objective, structured milestone dates/GPA and optional activities/languages without replacing legacy JSON section contracts.
- Explicit CLI `dotnet run --project UniNet.API -- --inspect-profile-email EMAIL` and `--seed-profile-email EMAIL`, with normal backend environment configured. Seed uses EF Core serializable transaction, exact normalized email lookup, fills only missing fields and empty JSON sections, preserves verified/privacy/populated data, never creates Account/User. Repeated seed is idempotent.
- Requested `quyenttse1704347@fpt.edu.vn` was absent in configured DB. Live seed was not run; supply correct email/database before seeding and inspect twice to confirm one profile. Seed twice is covered by SQLite tests.
- Backend build passes; 25 tests pass including owner permissions, public privacy, canonical references, idempotent seed, appearance creation without duplicates, and preservation of optional sections/stale editor appearance.
- Android currently points to deployed Render backend. This source change has not been deployed; verify live endpoint and authenticated Android save/readback after deployment.
- Full FE flow/cleanup/responsive validation: `UniNet_FE/docs/handoffs/production-profile.md` in the sibling repository.
