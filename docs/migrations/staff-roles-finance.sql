START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    DROP INDEX "IX_Accounts_SingleAdmin";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "Accounts" DROP CONSTRAINT "CK_Accounts_Role";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    UPDATE "Accounts" SET "SessionVersion" = "SessionVersion" + 1
    WHERE "Role" IN (2, 3, 4);
    UPDATE "RefreshTokens" SET "RevokedAt" = CURRENT_TIMESTAMP
    WHERE "AccountId" IN (SELECT "Id" FROM "Accounts" WHERE "Role" IN (2, 3, 4)) AND "RevokedAt" IS NULL;
    UPDATE "NotificationBroadcasts" SET "Role" = CASE "Role" WHEN 'Verificator' THEN 'Assessor' WHEN '2' THEN 'Admin' WHEN '3' THEN 'Moderator' WHEN '4' THEN 'Assessor' ELSE "Role" END;
    UPDATE "Accounts" SET "Role" = CASE "Role" WHEN 2 THEN 6 WHEN 3 THEN 4 WHEN 4 THEN 2 ELSE "Role" END;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "Approval" smallint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "ConfirmedByAccountId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "ManualBankReference" character varying(150);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "ManualPayingAccount" character varying(150);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "RequestKey" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "ReviewReason" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "ReviewedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD "ReviewedByAccountId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE TABLE "WalletWithdrawals" (
        "Id" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "Amount" numeric(18,0) NOT NULL,
        "BankName" character varying(150) NOT NULL,
        "BankAccountNumber" character varying(150) NOT NULL,
        "BankAccountHolder" character varying(150) NOT NULL,
        "State" smallint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ReviewedBy" uuid,
        "ReviewedAt" timestamp with time zone,
        "ReviewReason" character varying(2000),
        "OrderedBy" uuid,
        "OrderedAt" timestamp with time zone,
        "ConfirmedBy" uuid,
        "ConfirmedAt" timestamp with time zone,
        "PayingAccount" character varying(150),
        "BankReference" character varying(150),
        "Version" uuid NOT NULL,
        CONSTRAINT "PK_WalletWithdrawals" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_WalletWithdrawals_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "CK_WalletWithdrawals_State" CHECK ("State" BETWEEN 0 AND 5),
        CONSTRAINT "FK_WalletWithdrawals_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WalletWithdrawals_Accounts_ConfirmedBy" FOREIGN KEY ("ConfirmedBy") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WalletWithdrawals_Accounts_OrderedBy" FOREIGN KEY ("OrderedBy") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WalletWithdrawals_Accounts_ReviewedBy" FOREIGN KEY ("ReviewedBy") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE TABLE "WalletEntries" (
        "Id" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "Amount" numeric(18,0) NOT NULL,
        "SourceReference" character varying(150) NOT NULL,
        "Reason" character varying(2000) NOT NULL,
        "ActorAccountId" uuid NOT NULL,
        "WithdrawalId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_WalletEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_WalletEntries_Amount" CHECK ("Amount" <> 0 AND (("Amount" > 0 AND "WithdrawalId" IS NULL) OR ("Amount" < 0 AND "WithdrawalId" IS NOT NULL))),
        CONSTRAINT "FK_WalletEntries_Accounts_AccountId" FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WalletEntries_Accounts_ActorAccountId" FOREIGN KEY ("ActorAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_WalletEntries_WalletWithdrawals_WithdrawalId" FOREIGN KEY ("WithdrawalId") REFERENCES "WalletWithdrawals" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_RefundTransactions_ConfirmedByAccountId" ON "RefundTransactions" ("ConfirmedByAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_RefundTransactions_ManualPayingAccount_ManualBankReference" ON "RefundTransactions" ("ManualPayingAccount", "ManualBankReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_RefundTransactions_RequestKey" ON "RefundTransactions" ("RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_RefundTransactions_ReviewedByAccountId" ON "RefundTransactions" ("ReviewedByAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_Accounts_SingleAdmin" ON "Accounts" ("Role") WHERE "Role" = 6;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "Accounts" ADD CONSTRAINT "CK_Accounts_Role" CHECK ("Role" BETWEEN 0 AND 6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_WalletEntries_AccountId_SourceReference" ON "WalletEntries" ("AccountId", "SourceReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_WalletEntries_ActorAccountId" ON "WalletEntries" ("ActorAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_WalletEntries_WithdrawalId" ON "WalletEntries" ("WithdrawalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_WalletWithdrawals_AccountId_RequestKey" ON "WalletWithdrawals" ("AccountId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_WalletWithdrawals_ConfirmedBy" ON "WalletWithdrawals" ("ConfirmedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_WalletWithdrawals_OrderedBy" ON "WalletWithdrawals" ("OrderedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE UNIQUE INDEX "IX_WalletWithdrawals_PayingAccount_BankReference" ON "WalletWithdrawals" ("PayingAccount", "BankReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_WalletWithdrawals_ReviewedBy" ON "WalletWithdrawals" ("ReviewedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    CREATE INDEX "IX_WalletWithdrawals_State_CreatedAt" ON "WalletWithdrawals" ("State", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD CONSTRAINT "FK_RefundTransactions_Accounts_ConfirmedByAccountId" FOREIGN KEY ("ConfirmedByAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    ALTER TABLE "RefundTransactions" ADD CONSTRAINT "FK_RefundTransactions_Accounts_ReviewedByAccountId" FOREIGN KEY ("ReviewedByAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101140846_StaffRolesAndFinance') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261101140846_StaffRolesAndFinance', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101142007_PreserveWithdrawalRejectionAudit') THEN
    ALTER TABLE "WalletWithdrawals" ADD "RejectedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101142007_PreserveWithdrawalRejectionAudit') THEN
    ALTER TABLE "WalletWithdrawals" ADD "RejectedBy" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101142007_PreserveWithdrawalRejectionAudit') THEN
    ALTER TABLE "WalletWithdrawals" ADD "RejectionReason" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101142007_PreserveWithdrawalRejectionAudit') THEN
    CREATE INDEX "IX_WalletWithdrawals_RejectedBy" ON "WalletWithdrawals" ("RejectedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101142007_PreserveWithdrawalRejectionAudit') THEN
    ALTER TABLE "WalletWithdrawals" ADD CONSTRAINT "FK_WalletWithdrawals_Accounts_RejectedBy" FOREIGN KEY ("RejectedBy") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261101142007_PreserveWithdrawalRejectionAudit') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261101142007_PreserveWithdrawalRejectionAudit', '8.0.11');
    END IF;
END $EF$;
COMMIT;
