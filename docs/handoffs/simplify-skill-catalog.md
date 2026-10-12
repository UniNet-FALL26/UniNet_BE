# Simplified Skills catalog

## Result
User override to UniNet_Database_Design_v2.docx: remove Skills.Slug, Description and DisplayOrder. The account/profile module already matches the final six-table design; existing project tables are retained.

Skills now has seven columns: Id, Name, IconUrl, Category, IsActive, CreatedAt, UpdatedAt. Skill GUIDs remain canonical references for portfolio JSON and ProjectSkills. No new unique Name constraint was introduced. Category retains smallint enum/check range 0..8. Active catalog listing sorts by Name then Id and no longer returns slug. FE contract, development seed and tests match it.

## Migration
- `20261007141609_SimplifySkillCatalog`: drop the three columns, unique slug index and old Category/IsActive/DisplayOrder index; add Category/IsActive index. Historical migrations are retained.
- SQL: `docs/migrations/simplify-skill-catalog.sql`; reverse: `simplify-skill-catalog-rollback.sql`.
- Down recreates columns and supplies unique `skill-<Id>` slugs so rollback succeeds with multiple rows. Original removed values require backup restoration; down does not recover descriptions/order/old slugs.
- Applied successfully to the application DB configured in ignored `.env`. Existing six migrations were already applied; only this migration was pending.
- Before application, `pg_dump` archived Skills to ignored `artifacts/skills-before-simplification.dump`. It includes schema and table data. Preserve this local backup if original removed values may be needed.

## Verification
- Full backend suite: 47 passed. API build and FE TypeScript passed; existing unrelated project warning remains. FE editor tests: 8 passed.
- PostgreSQL migration/rollback fixture uses session-local temporary tables, shadowing live table names. Checks retained IDs/values, grouped index and both relational/JSON skill references. No application rows are used or changed by this fixture.
- Application DB verification: seven expected columns, new migration history entry, all three existing Skill IDs retained (same ordered-ID digest before/after).
- Backend must be restarted with the rebuilt assembly if an older API process is running; old queries reference removed columns.

## Main changes
Skill entity, DbContext mapping/snapshot, new EF migration, PortfolioSkillCatalog DTO/query, ProfileDevelopmentSeed, DatabaseDesignTests, PortfolioProductionTests, FE portfolio response type and browser fixture.
