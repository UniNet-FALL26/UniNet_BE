START TRANSACTION;

ALTER TABLE "Skills" DROP CONSTRAINT "CK_Skills_Category";

-- Convert the legacy nine categories without replacing IDs or references.
UPDATE "Skills"
SET "Category" = CASE
    WHEN "Category" = 2 THEN 0
    WHEN "Category" = 3 THEN 1
    WHEN "Category" = 6 THEN 2
    ELSE 3 END,
    "UpdatedAt" = CURRENT_TIMESTAMP
WHERE "Category" NOT IN (0, 1);

-- Frozen catalog from the profile templates; do not read mutable sample files here.
CREATE TEMP TABLE "skill_catalog_20261007152005" (
    "Name" text NOT NULL, "Category" smallint NOT NULL
) ON COMMIT DROP;
INSERT INTO "skill_catalog_20261007152005" VALUES
    ('React', 0), ('Next.js', 0), ('React Native', 0),
    ('TypeScript', 0), ('Tailwind CSS', 0), ('Zustand', 0),
    ('.NET', 1), ('ASP.NET Core', 1), ('Node.js', 1),
    ('PostgreSQL', 1), ('SQL Server', 1), ('Firebase', 1),
    ('Figma', 2), ('UI/UX', 2), ('Canva', 2),
    ('Photoshop', 2), ('Illustrator', 2), ('Premiere', 2),
    ('Git', 3), ('Docker', 3), ('Linux', 3),
    ('VS Code', 3), ('Postman', 3), ('SonarQube', 3);

UPDATE "Skills" AS existing
SET "Category" = seed."Category", "UpdatedAt" = CURRENT_TIMESTAMP
FROM "skill_catalog_20261007152005" AS seed
WHERE lower(btrim(existing."Name")) = lower(seed."Name")
    AND existing."Category" <> seed."Category";

INSERT INTO "Skills" ("Id", "Name", "IconUrl", "Category", "IsActive", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), seed."Name", NULL, seed."Category", TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM "skill_catalog_20261007152005" AS seed
WHERE NOT EXISTS (
    SELECT 1 FROM "Skills" AS existing
    WHERE lower(btrim(existing."Name")) = lower(seed."Name")
);
DROP TABLE pg_temp."skill_catalog_20261007152005";

ALTER TABLE "Skills" ADD CONSTRAINT "CK_Skills_Category" CHECK ("Category" BETWEEN 0 AND 3);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261007152005_PopulateFourGroupSkillCatalog', '8.0.11');

COMMIT;
