START TRANSACTION;

DROP INDEX "IX_Skills_Category_IsActive_DisplayOrder";

DROP INDEX "IX_Skills_Slug";

ALTER TABLE "Skills" DROP COLUMN "Description";

ALTER TABLE "Skills" DROP COLUMN "DisplayOrder";

ALTER TABLE "Skills" DROP COLUMN "Slug";

CREATE INDEX "IX_Skills_Category_IsActive" ON "Skills" ("Category", "IsActive");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261007141609_SimplifySkillCatalog', '8.0.11');

COMMIT;
