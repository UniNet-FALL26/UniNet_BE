# Database v2

## Scope
Source: UniNet_Database_Design_v2.docx, design dated 30/09/2026. Follow the final six-table design; section 7/13 adds Skills to the introductory five-table list. No new career/search/verification/admin endpoints were requested.

## Model
- Accounts and RefreshTokens keep their data/IDs and authentication logic.
- UserProfile remains the CLR type, mapped to Users. DbContext.Users is the primary new name; UserProfiles remains an alias for existing code/tests.
- Users adds FullName (255), Nickname (100), OrganizationName (255); TaxCode moves out of the public profile into verification JSON.
- UserVerifications preserves multiple attempts, jsonb snapshots/private document metadata, reviewer Account FK with restricted deletion, and UserId/Status index.
- CareerProfiles has unique UserId, optional creation, all ten jsonb sections, SkillsJson/ProjectsJson GIN indexes; IsPublic defaults false in the entity. No duplicate CV model.
- Skills keeps Id/Name/IconUrl/Category/IsActive/CreatedAt/UpdatedAt after the user's simplification. Slug, Description and DisplayOrder are removed by a later migration; grouped index is Category/IsActive. Category now has four values (Frontend, Backend, Design, ToolsAndOther), with 24 template skills seeded. SkillsJson references skillId; it is not a relational FK. See [simplify-skill-catalog.md](simplify-skill-catalog.md) and [four-group-skill-catalog.md](four-group-skill-catalog.md).
- JSON is stored as backend-controlled strings mapped to jsonb. Future write APIs must validate version/shape, skill references and authorization; no raw JSON write APIs are exposed here.

## Contracts and behavior
Partner registration uses organizationName, fullName, nickname, email, password, confirmPassword, partnerType. FullName is required; the backend derives Nickname if omitted, while the updated partner form asks for it explicitly. Student registration optionally accepts nickname, otherwise derives it from fullName.

Profile update requests use fullName and optional nickname; partner updates also require organizationName. Profile responses expose fullName/nickname/organizationName, and retain computed displayName = nickname for current clients. TaxCode and verification/document JSON are excluded. Existing integrations posting displayName or taxCode must migrate to these contracts.

Google signup initializes personal names from the Google display name and leaves partner OrganizationName unset until onboarding. Existing Google token verification/linking, JWTs, refresh rotation and logout logic remain in place.

Identity changes clear IsVerified but preserve history. Cosmetic nickname/avatar/cover/bio edits do not clear verification. Student updates retain shared Phone/Address fields.

## Migration
`20261003132450_AlignDatabaseDesignV2` renames the table/column without dropping profiles. Nickname derives from the first 100 characters of the old name; OrganizationName derives from the old partner name. Existing partner FullName needs correction because the old schema had only an organization/display name.

Legacy TaxCode or IsVerified creates a pending snapshot tagged `_migration: database-v2`, including old identity/contact fields and legacyIsVerified. Legacy verified flags are reset: there is no review history to justify an Approved request. Admin approval/reverification workflows are future work.

Down restores TaxCode/legacy flags before dropping new tables. It refuses rollback when career/skill/new verification data or separately edited names would be lost. Export/backup and an explicit migration are then required.

At initial implementation the application database was not updated because no connection was configured. On 07/10/2026 the configured `.env` database was verified to have v2 already applied, and the subsequent Skill simplification was applied successfully. For other environments, set ConnectionStrings__UniNet locally (do not commit secrets), then run from UniNet_BE:

```powershell
dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.Infrastructure
```

Reviewable incremental SQL: docs/migrations/database-v2.sql. Rollback: docs/migrations/database-v2-rollback.sql. Fresh databases use the full EF migration chain.

## Validation
- xUnit: 12 tests pass, including registration/login/refresh, separate partner names, invalid names, identity invalidation and PostgreSQL model metadata.
- Backend solution builds with zero warnings/errors; no pending model changes.
- Executed v1 fixture → v2 SQL → data/constraints/GIN/history assertions → rollback → restored-data assertions on isolated PostgreSQL 18. Rollback with new Skills data correctly fails without losing it.
- Auth TypeScript subset passes. Full frontend type-check fails in existing ProjectCreateForm/project.service code (missing datetimepicker dependency and enum values). Expo preview is blocked by the same missing datetimepicker plugin; no browser validation claim.
- Windows .NET build required DOTNET_PROCESSOR_COUNT=2, -m:1, -nodeReuse:false, -p:UseSharedCompilation=false to avoid stalled compiler workers.

## Repeating PostgreSQL fixture checks
On a disposable database only: apply v1 migration SQL; run UniNet.Tests/MigrationFixtures/database-v1.sql, then docs/migrations/database-v2.sql and database-v2-assert.sql with `psql -v ON_ERROR_STOP=1 -f <file>`. Apply database-v2-rollback.sql and run database-v1-rollback-assert.sql. Never seed these synthetic fixtures into an application database.
