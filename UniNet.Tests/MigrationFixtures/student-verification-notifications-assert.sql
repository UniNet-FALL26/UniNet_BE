\set ON_ERROR_STOP on
-- Isolated temporary tables; RunMigrationFixture.ps1 rolls back the entire fixture.
CREATE TEMP TABLE "UserVerifications" ("Id" uuid PRIMARY KEY);
CREATE TEMP TABLE "VerificationNotification" (
    "Id" uuid PRIMARY KEY, "VerificationId" uuid, "EvidenceModerationId" uuid, "Message" text,
    CONSTRAINT "CK_VerificationNotification_Target" CHECK (
        ("VerificationId" IS NOT NULL AND "EvidenceModerationId" IS NULL) OR
        ("VerificationId" IS NULL AND "EvidenceModerationId" IS NOT NULL))
);
CREATE TEMP TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
INSERT INTO "__EFMigrationsHistory" VALUES ('20261018003641_LinkVerificationToEvidenceModeration', '8.0.11');
INSERT INTO "VerificationNotification" VALUES
    ('10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', null, 'existing project'),
    ('10000000-0000-0000-0000-000000000002', null, '20000000-0000-0000-0000-000000000002', 'existing evidence');
CREATE TEMP TABLE original_notifications AS SELECT * FROM "VerificationNotification";
INSERT INTO "UserVerifications" VALUES ('30000000-0000-0000-0000-000000000001');

\ir ../../docs/migrations/student-verification-notifications.sql
\ir ../../docs/migrations/student-verification-notifications.sql

INSERT INTO "VerificationNotification" ("Id", "UserVerificationId", "Message")
    VALUES ('10000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000001', 'student submitted');
DO $$ BEGIN
    IF EXISTS (SELECT * FROM original_notifications EXCEPT
        SELECT "Id", "VerificationId", "EvidenceModerationId", "Message" FROM "VerificationNotification")
        THEN RAISE EXCEPTION 'Existing notifications changed'; END IF;
    IF (SELECT count(*) FROM "__EFMigrationsHistory") <> 2
        THEN RAISE EXCEPTION 'Migration history is not preserved/idempotent'; END IF;
    BEGIN
        INSERT INTO "VerificationNotification" ("Id", "UserVerificationId") VALUES (gen_random_uuid(), gen_random_uuid());
        RAISE EXCEPTION 'Unknown student verification accepted';
    EXCEPTION WHEN foreign_key_violation THEN NULL; END;
    BEGIN
        INSERT INTO "VerificationNotification" ("Id") VALUES (gen_random_uuid());
        RAISE EXCEPTION 'Missing notification target accepted';
    EXCEPTION WHEN check_violation THEN NULL; END;
    BEGIN
        INSERT INTO "VerificationNotification" ("Id", "VerificationId", "UserVerificationId")
            VALUES (gen_random_uuid(), gen_random_uuid(), '30000000-0000-0000-0000-000000000001');
        RAISE EXCEPTION 'Multiple notification targets accepted';
    EXCEPTION WHEN check_violation THEN NULL; END;
    BEGIN
        DELETE FROM "UserVerifications";
        RAISE EXCEPTION 'Referenced student verification deletion accepted';
    EXCEPTION WHEN foreign_key_violation OR restrict_violation THEN NULL; END;
END $$;
