# Four-group skill catalog

## Objective
Populate Skills with all 24 skills already used by the profile templates. Use exactly four categories: Frontend=0, Backend=1, Design=2, ToolsAndOther=3 (API label: Tools & Other). Six initial entries per group; future catalog entries remain supported. Preserve existing IDs, names, icons, active flags, portfolio JSON and project references.

## Mapping
Legacy Frontend/Mobile -> Frontend; Backend/Database -> Backend; Design -> Design; DevOps/Tools/AI/Other -> Tools & Other. For the 24 known names, canonical template classification takes precedence. Existing same-name records are reused case-insensitively with surrounding whitespace ignored; no new unique constraint or hard deletion.

## Plan and validation
1. Add failing enum/check-constraint and public category label regression tests.
2. Update enum, EF constraint, catalog label, development profile seed categories and sample label. Run xUnit and frontend checks.
3. Generate a new EF migration. Freeze the 24-name seed list in that migration; update existing matching entries and insert missing names. Verify PostgreSQL category conversion, no duplicate seed on repetition, IDs/relational/JSON references, active/icon preservation and rollback on session-local tables.
4. Back up Skills locally, apply to the configured DB and verify four groups and full catalog. Update handoff. No commits/push requested.

## Files and conventions
Domain enum; Infrastructure DbContext/migrations; Application PortfolioService/ProfileDevelopmentSeed; UniNet.Tests model/catalog/migration checks. FE sample JSON only; renderers already group by catalog label. Reuse existing EF migration conventions, e.g. `e.HasCheckConstraint("CK_Skills_Category", "\"Category\" BETWEEN 0 AND 3")`.

## Commands
`dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false --verbosity quiet`
`dotnet ef migrations add PopulateFourGroupSkillCatalog --project UniNet.Infrastructure --startup-project UniNet.Infrastructure --no-build`
`dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.Infrastructure`
Frontend: `npx tsc --noEmit`, focused sample/editor/render tests.

## Boundaries
DB/catalog update is authorized. No identity/profile seeding, new dependencies, removal of referenced rows or credential output. New entries may have null IconUrl; bundled FE logos already resolve all sample names. Reverse migration broadens the constraint and maps Design/Tools to legacy Design/Tools; split legacy categories need the backup to restore exactly. Keep seeded rows on rollback to avoid losing subsequently referenced skills.
