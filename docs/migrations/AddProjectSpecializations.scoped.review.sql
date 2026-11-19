START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    ALTER TABLE "Projects" ADD "SpecializationId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    CREATE TABLE "Specializations" (
        "Id" uuid NOT NULL,
        "Code" character varying(100) NOT NULL,
        "Name" character varying(255) NOT NULL,
        "IsActive" boolean NOT NULL,
        CONSTRAINT "PK_Specializations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    CREATE TABLE "AssessorSpecializations" (
        "AssessorUserId" uuid NOT NULL,
        "SpecializationId" uuid NOT NULL,
        CONSTRAINT "PK_AssessorSpecializations" PRIMARY KEY ("AssessorUserId", "SpecializationId"),
        CONSTRAINT "FK_AssessorSpecializations_Specializations_SpecializationId" FOREIGN KEY ("SpecializationId") REFERENCES "Specializations" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AssessorSpecializations_Users_AssessorUserId" FOREIGN KEY ("AssessorUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    INSERT INTO "Specializations" ("Id", "Code", "IsActive", "Name")
    VALUES ('a4900000-0000-0000-0000-000000000001', 'SOFTWARE_ENGINEERING', TRUE, 'Kỹ thuật phần mềm');
    INSERT INTO "Specializations" ("Id", "Code", "IsActive", "Name")
    VALUES ('a4900000-0000-0000-0000-000000000002', 'ARTIFICIAL_INTELLIGENCE', TRUE, 'Trí tuệ nhân tạo');
    INSERT INTO "Specializations" ("Id", "Code", "IsActive", "Name")
    VALUES ('a4900000-0000-0000-0000-000000000003', 'INFORMATION_SECURITY', TRUE, 'An toàn thông tin');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    CREATE INDEX "IX_Projects_SpecializationId" ON "Projects" ("SpecializationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    CREATE INDEX "IX_AssessorSpecializations_SpecializationId" ON "AssessorSpecializations" ("SpecializationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    CREATE UNIQUE INDEX "IX_Specializations_Code" ON "Specializations" ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    ALTER TABLE "Projects" ADD CONSTRAINT "FK_Projects_Specializations_SpecializationId" FOREIGN KEY ("SpecializationId") REFERENCES "Specializations" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109090826_AddProjectSpecializations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261109090826_AddProjectSpecializations', '8.0.11');
    END IF;
END $EF$;
COMMIT;

