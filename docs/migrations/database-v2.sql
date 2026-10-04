START TRANSACTION;

ALTER TABLE "UserProfiles" DROP CONSTRAINT "FK_UserProfiles_Accounts_AccountId";

DROP INDEX "IX_Accounts_GoogleId";

ALTER TABLE "UserProfiles" DROP CONSTRAINT "PK_UserProfiles";

ALTER TABLE "UserProfiles" DROP CONSTRAINT "CK_UserProfiles_PartnerType";

ALTER TABLE "UserProfiles" RENAME TO "Users";

ALTER TABLE "Users" RENAME COLUMN "DisplayName" TO "FullName";

ALTER INDEX "IX_UserProfiles_AccountId" RENAME TO "IX_Users_AccountId";

ALTER TABLE "Users" ADD "Nickname" character varying(100) NOT NULL DEFAULT '';

ALTER TABLE "Users" ADD "OrganizationName" character varying(255);

ALTER TABLE "Users" ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");

CREATE TABLE "CareerProfiles" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Headline" character varying(255),
    "CareerObjective" text,
    "BasicInfoJson" jsonb,
    "EducationJson" jsonb,
    "SkillsJson" jsonb,
    "ExperienceJson" jsonb,
    "ProjectsJson" jsonb,
    "CertificatesJson" jsonb,
    "ActivitiesJson" jsonb,
    "LanguagesJson" jsonb,
    "SocialLinksJson" jsonb,
    "AppearanceJson" jsonb,
    "IsPublic" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_CareerProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CareerProfiles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Skills" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Slug" character varying(100) NOT NULL,
    "IconUrl" text,
    "Category" smallint NOT NULL,
    "Description" text,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Skills" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Skills_Category" CHECK ("Category" BETWEEN 0 AND 8)
);

CREATE TABLE "UserVerifications" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "VerificationType" smallint NOT NULL,
    "Status" smallint NOT NULL,
    "VerificationData" jsonb NOT NULL,
    "DocumentUrls" jsonb,
    "RejectReason" text,
    "ReviewedBy" uuid,
    "SubmittedAt" timestamp with time zone NOT NULL,
    "ReviewedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UserVerifications" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_UserVerifications_Status" CHECK ("Status" BETWEEN 0 AND 3),
    CONSTRAINT "CK_UserVerifications_Type" CHECK ("VerificationType" BETWEEN 0 AND 1),
    CONSTRAINT "FK_UserVerifications_Accounts_ReviewedBy" FOREIGN KEY ("ReviewedBy") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserVerifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

UPDATE "Users" u SET "Nickname" = left(u."FullName", 100),
    "OrganizationName" = CASE WHEN a."Role" = 1 THEN u."FullName" ELSE NULL END
FROM "Accounts" a WHERE a."Id" = u."AccountId";

INSERT INTO "UserVerifications"
    ("Id", "UserId", "VerificationType", "Status", "VerificationData", "SubmittedAt", "CreatedAt", "UpdatedAt")
SELECT md5('uninet-v2-verification:' || u."Id"::text)::uuid, u."Id",
    CASE WHEN a."Role" = 1 THEN 1 ELSE 0 END, 0,
    jsonb_build_object('schemaVersion', 1, '_migration', 'database-v2',
        'fullName', u."FullName", 'organizationName', u."OrganizationName",
        'partnerType', u."PartnerType", 'taxCode', u."TaxCode",
        'universityName', u."UniversityName", 'major', u."Major", 'studentCode', u."StudentCode",
        'industry', u."Industry", 'website', u."Website", 'contactEmail', u."ContactEmail",
        'phone', u."Phone", 'address', u."Address", 'legacyIsVerified', u."IsVerified"),
    u."CreatedAt", u."CreatedAt", u."UpdatedAt"
FROM "Users" u JOIN "Accounts" a ON a."Id" = u."AccountId"
WHERE u."TaxCode" IS NOT NULL OR u."IsVerified";

UPDATE "Users" SET "IsVerified" = FALSE WHERE "IsVerified";

ALTER TABLE "Users" DROP COLUMN "TaxCode";

CREATE UNIQUE INDEX "IX_Accounts_GoogleId" ON "Accounts" ("GoogleId") WHERE "GoogleId" IS NOT NULL;

CREATE INDEX "IX_Users_UniversityName_Major" ON "Users" ("UniversityName", "Major");

ALTER TABLE "Users" ADD CONSTRAINT "CK_Users_PartnerType" CHECK ("PartnerType" IS NULL OR "PartnerType" BETWEEN 0 AND 4);

CREATE INDEX "IX_CareerProfiles_ProjectsJson" ON "CareerProfiles" USING gin ("ProjectsJson");

CREATE INDEX "IX_CareerProfiles_SkillsJson" ON "CareerProfiles" USING gin ("SkillsJson");

CREATE UNIQUE INDEX "IX_CareerProfiles_UserId" ON "CareerProfiles" ("UserId");

CREATE INDEX "IX_Skills_Category_IsActive_DisplayOrder" ON "Skills" ("Category", "IsActive", "DisplayOrder");

CREATE UNIQUE INDEX "IX_Skills_Slug" ON "Skills" ("Slug");

CREATE INDEX "IX_UserVerifications_ReviewedBy" ON "UserVerifications" ("ReviewedBy");

CREATE INDEX "IX_UserVerifications_UserId_Status" ON "UserVerifications" ("UserId", "Status");

ALTER TABLE "Users" ADD CONSTRAINT "FK_Users_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261003132450_AlignDatabaseDesignV2', '8.0.11');

COMMIT;

