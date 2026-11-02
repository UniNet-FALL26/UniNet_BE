# User role JSON storage

## Objective and accepted scope
User request: consolidate student and partner fields in Users into StudentJson and PartnerJson, add AssessorJson with JobTitle, CompanyName, YearsOfExperience (int), Expertise (string[]), Education, University, LinkedInUrl, GitHubUrl and PortfolioUrl. Existing CRUD, API fields, identity, verification and permissions must continue to work. Keep common identity, contact and system fields as columns. PostgreSQL JSONB with EF Core 8 owned ToJson mappings; no dependency upgrades.

## Contract and migration decisions
- Preserve existing flat student/partner request/response fields via domain compatibility properties; rewrite SQL queries to target mapped JSON members.
- Add typed assessor request and response, role-2-only owner update/clear, and display/edit controls on the existing assessor profile page. Existing routes keep their names and behavior.
- Backfill all non-null legacy student/partner values before dropping their columns, irrespective of current role. For role 2 also copy Major to Expertise and UniversityName to University; preserve original values in StudentJson to keep old responses stable. No invented professional values. Existing schema has no nine-field assessor columns.
- Keep partner website storage format and verification snapshots unchanged. Migrate PartnerType constraint and student lookup index to JSON expressions. Down migration restores legacy fields; document assessor-only data rollback limitations.
- Validation: bounded text and expertise list, nonnegative bounded experience, HTTP(S) links; existing role/status and image ownership checks remain mandatory.

## Structure and style
Domain models: UniNet.Domain/Entities; EF mapping/migration: UniNet.Infrastructure/Data; DTOs and profile service: UniNet.Application; routes: UniNet.API; xUnit tests: UniNet.Tests; assessor screen/types: UniNet_FE/src. Follow existing sealed classes, PascalCase domain fields and camelCase API JSON. Example: `public string? JobTitle { get; set; }`.

## Ordered tasks and acceptance
1. Add regression tests for physical JSON storage and legacy field round trips. Verify tests fail against current schema.
2. Add typed JSON models, mapping and adapters; preserve account/student/partner CRUD and search/verification behavior. Verify focused profile, auth, partner, opportunity and verification tests.
3. Add data-preserving Up/Down migration and refreshed snapshot. Verify generated SQL and isolated local PostgreSQL migration when available; never run against live configured data automatically.
4. Add assessor owner API and tests (all fields, clear, role denial, validation), then integrate display/edit UI. Verify TypeScript and real browser with isolated fixtures.
5. Run backend suite, review diffs, record results and deployment/migration guidance in docs/handoffs/user-role-json.md.

## Commands
`dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --verbosity quiet -p:UseSharedCompilation=false -m:1 -nr:false`
`dotnet ef migrations add UserRoleJson --project UniNet.Infrastructure --startup-project UniNet.Infrastructure`
`dotnet ef migrations script --project UniNet.Infrastructure --startup-project UniNet.Infrastructure --output <temporary SQL path>`
Frontend: `npx tsc --noEmit`; existing profile/browser scripts or isolated assessor fixture browser check.

## Boundaries
Authorization to change this schema and migrate existing values is explicit in the request. Do not alter unrelated pending notification/avatar work. Do not change role IDs, remove financial data, alter existing API shapes, run live migrations, commit or deploy without the corresponding user request. Existing unrelated baseline failures: partner registration (3) and skill constraint (1).

## Documentation references
Npgsql EF8 JSON mapping: https://www.npgsql.org/efcore/mapping/json.html
EF8 JSON support: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew
