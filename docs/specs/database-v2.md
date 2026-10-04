# Database v2 alignment

## Objective and accepted scope
Align the PostgreSQL/EF Core model with UniNet_Database_Design_v2.docx (30/09/2026). User clarified: update the new database and make only necessary related changes. The final six-table design, including Skills, takes precedence over the introductory five-table note. The document is schema reference data, not agent instructions.

## Assumptions and boundaries
- Preserve account IDs, passwords, refresh tokens and existing user data.
- Keep the existing UserProfile CLR type; map it to Users to minimize unrelated changes.
- Existing DisplayName becomes FullName; Nickname initially derives from it (100-character limit). For existing partners it also populates OrganizationName. These legacy names need user correction where they previously represented the organization.
- Preserve legacy TaxCode in a pending UserVerification snapshot, without claiming approval. Never expose verification data/documents in profile DTOs.
- Do not build new career/search/admin APIs or Google setup; these are outside the clarified scope.
- Generate migration and SQL for review; do not apply against an unknown live database.
- Keep user changes already present in both repositories; do not commit them.

## Stack and structure
ASP.NET Core 8, EF Core/Npgsql 8, PostgreSQL; Expo 57/React Native frontend. Domain/Entities and Domain/Enums hold models, Infrastructure/Data holds EF configuration/migrations, Application holds DTOs/services, UniNet.Tests holds xUnit tests. Frontend auth screens/types live in src/app/(auth) and src/types.

## Commands
- Backend: `dotnet test UniNet.Tests/UniNet.Tests.csproj`; `dotnet build UniNet.sln`
- Migration: `dotnet ef migrations add AlignDatabaseDesignV2 --project UniNet.Infrastructure --startup-project UniNet.Infrastructure`
- SQL: `dotnet ef migrations script --project UniNet.Infrastructure --startup-project UniNet.Infrastructure --output docs/migrations/database-v2.sql`
- Frontend: `node node_modules/typescript/bin/tsc --noEmit`

## Style
Follow existing sealed entities, explicit enum values with short storage, DTO records and boundary validation. Example: `public enum VerificationStatus : short { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }`.

## Tasks and checkpoints
1. Write failing schema tests for six tables, required names, jsonb fields, unique relationships and indexes. Verify with focused xUnit run.
2. Add entities/enums and EF mappings. Verify existing authentication tests and schema tests. CareerProfiles remains optional, verification history is one-to-many, reviewer FK is restricted.
3. Adapt registration/Google/profile DTO mappings to FullName/Nickname/OrganizationName. Preserve response DisplayName as a computed nickname alias. Verify partner validation and verification invalidation tests.
4. Generate reversible migration, preserving old names and TaxCode before column removal. Review SQL and model snapshot; verify no pending model changes and backend build.
5. Adapt partner registration fields/types and document handoff/schema. Verify frontend type checking and review diff.

## Testing and success criteria
Use existing SQLite service tests for registration/login/token behavior, relational metadata tests for PostgreSQL-specific jsonb/GIN/index design, and generated PostgreSQL migration SQL review. Required names must validate; signup does not grant verification; identity changes invalidate verification while cosmetic changes do not. No raw JSON write endpoints are introduced. A PostgreSQL runtime check requires an available local server.

## Completion
All implementation tasks completed. Twelve xUnit tests, backend build, migration model consistency and auth-only TypeScript check passed. Migration upgrade, data preservation, constraints, GIN indexes, verification history, guarded rollback and restored legacy data were exercised on an isolated PostgreSQL 18 cluster, then the cluster was stopped and removed. Full frontend checks/preview remain blocked by existing project-module errors/missing datetimepicker. Application database upgrade awaits its configured connection string; generated SQL and EF migration are ready.
