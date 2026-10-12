\set ON_ERROR_STOP on
-- Session-local tables shadow the application tables: no real rows are modified.
CREATE TEMP TABLE "Skills" (
    "Id" uuid PRIMARY KEY, "Name" varchar(100) NOT NULL, "Slug" varchar(100) NOT NULL,
    "IconUrl" text, "Category" smallint NOT NULL CHECK ("Category" BETWEEN 0 AND 8),
    "Description" text, "DisplayOrder" integer NOT NULL, "IsActive" boolean NOT NULL,
    "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL
);
CREATE UNIQUE INDEX "IX_Skills_Slug" ON "Skills" ("Slug");
CREATE INDEX "IX_Skills_Category_IsActive_DisplayOrder" ON "Skills" ("Category", "IsActive", "DisplayOrder");
CREATE TEMP TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
CREATE TEMP TABLE "ProjectSkills" ("SkillId" uuid REFERENCES "Skills" ("Id"));
CREATE TEMP TABLE "CareerProfiles" ("SkillsJson" jsonb);
INSERT INTO "Skills" VALUES
('10000000-0000-0000-0000-000000000001','React','react','https://images.example.org/react.png',0,'Legacy description',8,true,'2026-01-01','2026-01-02'),
('10000000-0000-0000-0000-000000000002','Archived','archived',null,5,null,2,false,'2026-01-01','2026-01-02');
INSERT INTO "ProjectSkills" VALUES ('10000000-0000-0000-0000-000000000001');
INSERT INTO "CareerProfiles" VALUES ('[{"skillId":"10000000-0000-0000-0000-000000000001","level":2}]');

\ir ../../docs/migrations/simplify-skill-catalog.sql
DO $$ BEGIN
    IF (SELECT count(*) FROM "Skills") <> 2 OR NOT EXISTS (
        SELECT 1 FROM "Skills" WHERE "Id" = '10000000-0000-0000-0000-000000000001'
        AND "Name" = 'React' AND "IconUrl" = 'https://images.example.org/react.png'
        AND "Category" = 0 AND "IsActive" AND "CreatedAt" = '2026-01-01' AND "UpdatedAt" = '2026-01-02'
    ) THEN RAISE EXCEPTION 'Skill values or IDs changed'; END IF;
    IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'pg_temp."Skills"'::regclass
        AND NOT attisdropped AND attname IN ('Slug','Description','DisplayOrder'))
        THEN RAISE EXCEPTION 'Removed columns still exist'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname LIKE 'pg_temp_%' AND indexname = 'IX_Skills_Category_IsActive')
        THEN RAISE EXCEPTION 'New grouped index missing'; END IF;
    IF (SELECT count(*) FROM "ProjectSkills" s JOIN "Skills" c ON c."Id" = s."SkillId") <> 1
        OR (SELECT "SkillsJson"->0->>'skillId' FROM "CareerProfiles") <> '10000000-0000-0000-0000-000000000001'
        THEN RAISE EXCEPTION 'Skill references changed'; END IF;
END $$;

\ir ../../docs/migrations/simplify-skill-catalog-rollback.sql
DO $$ BEGIN
    IF (SELECT count(DISTINCT "Slug") FROM "Skills") <> 2
        OR EXISTS (SELECT 1 FROM "Skills" WHERE "Slug" <> 'skill-' || "Id"::text)
        THEN RAISE EXCEPTION 'Rollback cannot create unique slugs for existing rows'; END IF;
    IF (SELECT count(*) FROM "ProjectSkills" s JOIN "Skills" c ON c."Id" = s."SkillId") <> 1
        THEN RAISE EXCEPTION 'Rollback lost skill references'; END IF;
END $$;
SELECT 'Skill migration and rollback passed with preserved IDs/references' AS result;
