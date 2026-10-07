\set ON_ERROR_STOP on
-- Run with RunMigrationFixture.ps1 to pin one transaction and roll back all test state.
CREATE TEMP TABLE "Skills" (
    "Id" uuid PRIMARY KEY, "Name" varchar(100) NOT NULL, "IconUrl" text,
    "Category" smallint NOT NULL CONSTRAINT "CK_Skills_Category" CHECK ("Category" BETWEEN 0 AND 8),
    "IsActive" boolean NOT NULL, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL
);
CREATE TEMP TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
CREATE TEMP TABLE "ProjectSkills" ("SkillId" uuid REFERENCES "Skills" ("Id"));
CREATE TEMP TABLE "CareerProfiles" ("SkillsJson" jsonb);
INSERT INTO "Skills" ("Id","Name","Category","IconUrl","IsActive","CreatedAt","UpdatedAt")
SELECT ('10000000-0000-0000-0000-' || lpad(n::text,12,'0'))::uuid, name, category,
    'https://images.example.org/keep.png', n <> 1, '2026-01-01', '2026-01-02'
FROM (VALUES (1,' react ',0),(2,'Custom backend',1),(3,'React Native',2),
    (4,'PostgreSQL',3),(5,'Docker',4),(6,'Git',5),(7,'Figma',6),
    (8,'Custom AI',7),(9,'Custom other',8)) AS legacy(n,name,category);
CREATE TEMP TABLE original_skills AS SELECT * FROM "Skills";
INSERT INTO "ProjectSkills" VALUES ('10000000-0000-0000-0000-000000000007');
INSERT INTO "CareerProfiles" VALUES ('[{"skillId":"10000000-0000-0000-0000-000000000001","level":2}]');

\ir ../../docs/migrations/four-group-skill-catalog.sql
DO $$ BEGIN
    IF (SELECT count(*) FROM "Skills") <> 27 OR (SELECT count(DISTINCT "Category") FROM "Skills") <> 4
        THEN RAISE EXCEPTION 'Missing catalog rows or category groups'; END IF;
    IF EXISTS (SELECT 1 FROM original_skills o LEFT JOIN "Skills" s ON s."Id"=o."Id"
        WHERE s."Id" IS NULL OR s."Name" <> o."Name" OR s."IconUrl" <> o."IconUrl"
        OR s."IsActive" <> o."IsActive" OR s."CreatedAt" <> o."CreatedAt")
        THEN RAISE EXCEPTION 'Existing identity/icon/active/creation metadata changed'; END IF;
    IF EXISTS (SELECT 1 FROM original_skills o JOIN "Skills" s ON s."Id"=o."Id"
        WHERE s."Category" <> CASE o."Category" WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 2 THEN 0 WHEN 3 THEN 1 WHEN 6 THEN 2 ELSE 3 END)
        THEN RAISE EXCEPTION 'Legacy category mapping failed'; END IF;
    IF EXISTS (SELECT lower(btrim("Name")) FROM "Skills" GROUP BY lower(btrim("Name")) HAVING count(*) > 1)
        THEN RAISE EXCEPTION 'Case-insensitive existing catalog rows were duplicated'; END IF;
    IF NOT EXISTS (SELECT 1 FROM "Skills" WHERE "Name"='UI/UX' AND "Category"=2)
        OR NOT EXISTS (SELECT 1 FROM "Skills" WHERE "Name"='Tailwind CSS' AND "Category"=0)
        OR NOT EXISTS (SELECT 1 FROM "Skills" WHERE "Name"='SonarQube' AND "Category"=3)
        THEN RAISE EXCEPTION 'Template skills seeded incorrectly'; END IF;
    BEGIN
        INSERT INTO "Skills" VALUES (gen_random_uuid(),'Invalid',null,4,true,now(),now());
        RAISE EXCEPTION 'Out-of-range category accepted';
    EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
CREATE TEMP TABLE first_seed_ids AS SELECT "Id" FROM "Skills";

\ir ../../docs/migrations/four-group-skill-catalog-rollback.sql
DO $$ BEGIN
    IF (SELECT count(*) FROM "Skills") <> 27 OR EXISTS (SELECT 1 FROM "Skills" WHERE "Category" NOT IN (0,1,5,6))
        THEN RAISE EXCEPTION 'Rollback removed referenced skills or failed to restore legacy categories'; END IF;
END $$;

\ir ../../docs/migrations/four-group-skill-catalog.sql
DO $$ BEGIN
    IF (SELECT count(*) FROM "Skills") <> 27 OR EXISTS (SELECT "Id" FROM first_seed_ids EXCEPT SELECT "Id" FROM "Skills")
        THEN RAISE EXCEPTION 'Reseeding duplicated or replaced skill IDs'; END IF;
    IF (SELECT count(*) FROM "ProjectSkills" p JOIN "Skills" s ON s."Id"=p."SkillId") <> 1
        OR (SELECT "SkillsJson"->0->>'skillId' FROM "CareerProfiles") <> '10000000-0000-0000-0000-000000000001'
        THEN RAISE EXCEPTION 'Project/portfolio references changed'; END IF;
END $$;
SELECT 'Four-group catalog migration, reseeding and rollback passed' AS result;
