START TRANSACTION;

ALTER TABLE "Skills" DROP CONSTRAINT "CK_Skills_Category";

UPDATE "Skills" SET "Category" = CASE "Category" WHEN 2 THEN 6 WHEN 3 THEN 5 ELSE "Category" END,
    "UpdatedAt" = CURRENT_TIMESTAMP
WHERE "Category" IN (2, 3);

ALTER TABLE "Skills" ADD CONSTRAINT "CK_Skills_Category" CHECK ("Category" BETWEEN 0 AND 8);

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261007152005_PopulateFourGroupSkillCatalog';

COMMIT;
