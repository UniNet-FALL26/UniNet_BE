DO $$ BEGIN
    IF (SELECT count(*) FROM "Users") <> 2
        OR (SELECT "FullName" FROM "Users" WHERE "Id" = '00000000-0000-0000-0000-000000000011') <> repeat('A', 150)
        OR (SELECT length("Nickname") FROM "Users" WHERE "Id" = '00000000-0000-0000-0000-000000000011') <> 100
        OR (SELECT "OrganizationName" FROM "Users" WHERE "Id" = '00000000-0000-0000-0000-000000000012') <> 'ABC Company'
        OR EXISTS (SELECT 1 FROM "Users" WHERE "IsVerified")
        OR (SELECT "PasswordHash" FROM "Accounts" WHERE "Id" = '00000000-0000-0000-0000-000000000002') <> 'preserved-partner-hash'
        OR (SELECT "TokenHash" FROM "RefreshTokens") <> 'preserved-token-hash'
        OR (SELECT count(*) FROM "UserVerifications") <> 1
        OR (SELECT "VerificationData"->>'taxCode' FROM "UserVerifications") <> '0312345678'
        OR (SELECT "Status" FROM "UserVerifications") <> 0
        OR EXISTS (SELECT 1 FROM "CareerProfiles")
    THEN RAISE EXCEPTION 'Migration lost or incorrectly transformed legacy data'; END IF;
    IF (SELECT count(*) FROM pg_indexes WHERE tablename = 'CareerProfiles' AND indexdef LIKE '%USING gin%') <> 2
    THEN RAISE EXCEPTION 'Missing career GIN indexes'; END IF;
END $$;

BEGIN;
INSERT INTO "CareerProfiles" ("Id", "UserId", "SkillsJson", "IsPublic", "CreatedAt", "UpdatedAt") VALUES
('00000000-0000-0000-0000-000000000031', '00000000-0000-0000-0000-000000000011', '[{"skillId":"00000000-0000-0000-0000-000000000041","level":2}]', false, now(), now());
DO $$ BEGIN
    BEGIN
        INSERT INTO "CareerProfiles" ("Id", "UserId", "IsPublic", "CreatedAt", "UpdatedAt") VALUES
        ('00000000-0000-0000-0000-000000000032', '00000000-0000-0000-0000-000000000011', false, now(), now());
        RAISE EXCEPTION 'Duplicate career profile was accepted';
    EXCEPTION WHEN unique_violation THEN NULL; END;
END $$;
INSERT INTO "UserVerifications" ("Id", "UserId", "VerificationType", "Status", "VerificationData", "SubmittedAt", "CreatedAt", "UpdatedAt") VALUES
('00000000-0000-0000-0000-000000000051', '00000000-0000-0000-0000-000000000012', 1, 2, '{"schemaVersion":1,"organizationName":"ABC Company"}', now(), now(), now());
DO $$ BEGIN
    IF (SELECT count(*) FROM "UserVerifications" WHERE "UserId" = '00000000-0000-0000-0000-000000000012') <> 2
    THEN RAISE EXCEPTION 'Verification history must allow multiple attempts'; END IF;
END $$;
ROLLBACK;
