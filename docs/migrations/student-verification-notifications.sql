-- Apply the missing student notification migration, including when newer migrations already exist.
-- Run with psql -X -v ON_ERROR_STOP=1 -f docs/migrations/student-verification-notifications.sql
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20261017152032_AddStudentVerificationNotifications') THEN
        ALTER TABLE "VerificationNotification" DROP CONSTRAINT "CK_VerificationNotification_Target";
        ALTER TABLE "VerificationNotification" ADD "UserVerificationId" uuid;
        CREATE INDEX "IX_VerificationNotification_UserVerificationId" ON "VerificationNotification" ("UserVerificationId");
        ALTER TABLE "VerificationNotification" ADD CONSTRAINT "CK_VerificationNotification_Target" CHECK (
            (CASE WHEN "VerificationId" IS NOT NULL THEN 1 ELSE 0 END +
             CASE WHEN "EvidenceModerationId" IS NOT NULL THEN 1 ELSE 0 END +
             CASE WHEN "UserVerificationId" IS NOT NULL THEN 1 ELSE 0 END) = 1);
        ALTER TABLE "VerificationNotification" ADD CONSTRAINT "FK_VerificationNotification_UserVerifications_UserVerification~"
            FOREIGN KEY ("UserVerificationId") REFERENCES "UserVerifications" ("Id") ON DELETE RESTRICT;
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20261017152032_AddStudentVerificationNotifications', '8.0.11');
    END IF;
END $$;
COMMIT;
