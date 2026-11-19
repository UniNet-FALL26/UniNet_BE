# Remove profile visibility

- Removed `CareerProfile.IsPublic`, `PortfolioContent.IsPublic`, frontend type/sample/fixtures, and the visibility predicate in `PortfolioService.Read`.
- Active student profiles can be read without an existing career profile. Suspended accounts and non-student profile requests remain unavailable. Hidden sections and `ShowPortfolio` presentation settings still apply.
- Community author links include `source=community`; the profile viewer also displays that author's published community posts.
- Migration: `20261119031147_RemoveCareerProfileVisibility` drops only `CareerProfiles.IsPublic`. Down restores a non-null boolean defaulting to true for compatibility.
- Validation: 55 backend portfolio/HTTP tests; 15 frontend draft/editor tests; browser author name/avatar navigation, actual profile rendering, public author posts, and noninteractive advertiser identity.

## Deployment status (2026-11-19)

- Migration was applied to the configured external project DB.
- Live Render API Swagger still includes `PortfolioContent.isPublic`, indicating an older deployed backend; it must be redeployed to match the changed DB.
- A compatibility rollback to `20261118060015_RemoveProjectExpectedEndDates` was requested but rejected by automatic approval review because restoration was not explicitly authorized. It was NOT executed. User approval is pending.
- Do not claim the live API is repaired until the deployment or approved compatibility rollback has completed and live requests are verified.
