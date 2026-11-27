# Profile interests persistence

- `PortfolioContent.Interests` is optional for writes. The existing owner editor/save endpoints persist it in `CareerProfile.BasicInfoJson`, preserving unrelated JSON metadata. No schema migration.
- Missing/null keeps existing values; an explicit empty array clears them. Owner/public responses return an array, defaulting to empty for legacy profiles.
- `PortfolioInterestCatalog.Names` defines 48 allowed names and matches the frontend catalog. Validation rejects unknown/null/blank/duplicate entries and oversized lists before any identity/content writes.
- Added `interests` to valid section keys and visitor/application snapshot filtering. Existing write authorization remains unchanged.
- Verified with SQLite persistence/reload/legacy/clear/hidden-section tests and real HTTP tests for owner save, anonymous/partner/unknown-user rejection, public reads and invalid-write atomicity. Portfolio suite: 57 passing tests; additional HTTP test also passed.
