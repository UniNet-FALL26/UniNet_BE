START TRANSACTION;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM "CareerProfiles") OR EXISTS (SELECT 1 FROM "Skills")
        OR EXISTS (SELECT 1 FROM "UserVerifications"
            WHERE "VerificationData"->>'_migration' IS DISTINCT FROM 'database-v2' OR "Status" <> 0)
        OR EXISTS (SELECT 1 FROM "Users" u JOIN "Accounts" a ON a."Id" = u."AccountId"
            WHERE u."Nickname" IS DISTINCT FROM left(u."FullName", 100)
                OR (a."Role" = 1 AND u."FullName" IS DISTINCT FROM u."OrganizationName"))
        OR EXISTS (SELECT 1 FROM "Users" u JOIN "UserVerifications" v ON v."UserId" = u."Id"
            WHERE u."FullName" IS DISTINCT FROM v."VerificationData"->>'fullName'
                OR u."OrganizationName" IS DISTINCT FROM v."VerificationData"->>'organizationName')
    THEN RAISE EXCEPTION 'Database v2 contains new business data; export it before rollback.';
    END IF;
END $$;

ALTER TABLE "Users" ADD "TaxCode" character varying(50);

UPDATE "Users" u SET "TaxCode" = v."VerificationData"->>'taxCode',
    "IsVerified" = COALESCE((v."VerificationData"->>'legacyIsVerified')::boolean, FALSE)
FROM "UserVerifications" v WHERE v."UserId" = u."Id"
    AND v."VerificationData"->>'_migration' = 'database-v2';
UPDATE "Users" u SET "FullName" = u."OrganizationName"
FROM "Accounts" a WHERE a."Id" = u."AccountId" AND a."Role" = 1 AND u."OrganizationName" IS NOT NULL;

ALTER TABLE "Users" DROP CONSTRAINT "FK_Users_Accounts_AccountId";

DROP TABLE "CareerProfiles";

DROP TABLE "Skills";

DROP TABLE "UserVerifications";

DROP INDEX "IX_Accounts_GoogleId";

ALTER TABLE "Users" DROP CONSTRAINT "PK_Users";

DROP INDEX "IX_Users_UniversityName_Major";

ALTER TABLE "Users" DROP CONSTRAINT "CK_Users_PartnerType";

ALTER TABLE "Users" DROP COLUMN "Nickname";

ALTER TABLE "Users" DROP COLUMN "OrganizationName";

ALTER TABLE "Users" RENAME TO "UserProfiles";

ALTER TABLE "UserProfiles" RENAME COLUMN "FullName" TO "DisplayName";

ALTER INDEX "IX_Users_AccountId" RENAME TO "IX_UserProfiles_AccountId";

ALTER TABLE "UserProfiles" ADD CONSTRAINT "PK_UserProfiles" PRIMARY KEY ("Id");

CREATE UNIQUE INDEX "IX_Accounts_GoogleId" ON "Accounts" ("GoogleId");

ALTER TABLE "UserProfiles" ADD CONSTRAINT "CK_UserProfiles_PartnerType" CHECK ("PartnerType" IS NULL OR "PartnerType" BETWEEN 0 AND 4);

ALTER TABLE "UserProfiles" ADD CONSTRAINT "FK_UserProfiles_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE;

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261003132450_AlignDatabaseDesignV2';

COMMIT;

