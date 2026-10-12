START TRANSACTION;

DROP INDEX "IX_Skills_Category_IsActive";

ALTER TABLE "Skills" ADD "Description" text;

ALTER TABLE "Skills" ADD "DisplayOrder" integer NOT NULL DEFAULT 0;

ALTER TABLE "Skills" ADD "Slug" character varying(100) NOT NULL DEFAULT '';

UPDATE "Skills" SET "Slug" = 'skill-' || "Id"::text;

CREATE INDEX "IX_Skills_Category_IsActive_DisplayOrder" ON "Skills" ("Category", "IsActive", "DisplayOrder");

CREATE UNIQUE INDEX "IX_Skills_Slug" ON "Skills" ("Slug");

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261007141609_SimplifySkillCatalog';

COMMIT;
