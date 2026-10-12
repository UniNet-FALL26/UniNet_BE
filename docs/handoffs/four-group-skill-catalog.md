# Four-group Skills catalog

## Delivered
All 24 skills in the current FE profile samples are present in the application Skills table, six per group:
- 0 Frontend: React, Next.js, React Native, TypeScript, Tailwind CSS, Zustand.
- 1 Backend: .NET, ASP.NET Core, Node.js, PostgreSQL, SQL Server, Firebase.
- 2 Design: Figma, UI/UX, Canva, Photoshop, Illustrator, Premiere.
- 3 ToolsAndOther (API display label Tools & Other): Git, Docker, Linux, VS Code, Postman, SonarQube.

Legacy category mapping: Frontend/Mobile -> Frontend; Backend/Database -> Backend; Design -> Design; DevOps/Tools/AI/Other -> ToolsAndOther. Enum and CK_Skills_Category now allow only 0..3. API returns the readable Tools & Other label; FE sample label is synchronized. Existing renderers already group/filter by category strings.

## Persistence
Applied `20261007152005_PopulateFourGroupSkillCatalog` to the configured DB. Original public Skills rows React/.NET/Figma were preserved, including IDs/icons/active flags/creation timestamps; 21 rows were added. Known template names are matched case-insensitively after trimming, avoiding new duplicates. Existing inactive entries are not reactivated. Newly inserted IconUrl is null; FE bundled logos cover the template names.

Frozen seed list lives in the migration, independent of future sample edits. No new Skill columns, dependencies or profile/account writes. No commit/push performed for this task.

Backup: ignored `artifacts/skills-before-four-groups.dump`. SQL/rollback: `docs/migrations/four-group-skill-catalog.sql` and `four-group-skill-catalog-rollback.sql`. Rollback broadens the constraint and maps Design/Tools back to legacy Design/Tools, preserving seeded rows that may be referenced. Exact old category splits require backup restoration.

## Verification
- 54 backend tests pass; API build passes with existing unrelated project warnings. EF model has no pending changes.
- FE TypeScript and 29 focused sample/editor/render tests pass.
- PostgreSQL fixture covers all nine legacy categories, canonical names with case/whitespace variations, inactive/icon/creation preservation, 24 template entries, rejection of category 4, stable IDs across rollback/reapply and relational/JSON references.
- Run SQL fixtures using `UniNet.Tests/MigrationFixtures/RunMigrationFixture.ps1` (PG environment credentials), passing `-Fixture four-group-skill-catalog-assert.sql -PsqlPath <psql executable>`. It expands generated SQL and strips their transaction boundaries, pins one transaction and rolls back all test state. This prevents temporary test tables persisting across a transaction-pooler server session. Initial test temporary state was cleaned up; live verification uses explicit public-qualified queries.
- Live public table/history confirms 24 active rows, exactly six in each category and the new migration entry. Restart an older running API process to pick up the new enum/labels.
