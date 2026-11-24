START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    ALTER TABLE "GitSyncJobs" ADD CONSTRAINT "AK_GitSyncJobs_Id_ProjectGitRepositoryId" UNIQUE ("Id", "ProjectGitRepositoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    ALTER TABLE "GitSyncCheckpoints" ADD CONSTRAINT "AK_GitSyncCheckpoints_Id_SyncJobId" UNIQUE ("Id", "SyncJobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE TABLE "ProjectGitEvidenceSnapshotCoverage" (
        "SnapshotId" uuid NOT NULL,
        "GitSyncCheckpointId" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "ProjectGitRepositoryId" uuid NOT NULL,
        "GitSyncJobId" uuid NOT NULL,
        "Category" character varying(100) NOT NULL,
        "ScopeKey" character varying(512) NOT NULL,
        "ItemsProcessed" bigint NOT NULL,
        "ScopeCompletedAt" timestamp with time zone NOT NULL,
        "JobCompletedAt" timestamp with time zone NOT NULL,
        "ReceiptPolicyVersion" integer NOT NULL,
        CONSTRAINT "PK_ProjectGitEvidenceSnapshotCoverage" PRIMARY KEY ("SnapshotId", "GitSyncCheckpointId"),
        CONSTRAINT "CK_SnapshotCoverage_Receipt" CHECK ("ItemsProcessed" >= 0 AND "ReceiptPolicyVersion" = 1 AND "ScopeCompletedAt" <= "JobCompletedAt"),
        CONSTRAINT "FK_ProjectGitEvidenceSnapshotCoverage_GitSyncCheckpoints_GitSy~" FOREIGN KEY ("GitSyncCheckpointId", "GitSyncJobId") REFERENCES "GitSyncCheckpoints" ("Id", "SyncJobId") ON DELETE RESTRICT,
        CONSTRAINT "FK_ProjectGitEvidenceSnapshotCoverage_GitSyncJobs_GitSyncJobId~" FOREIGN KEY ("GitSyncJobId", "ProjectGitRepositoryId") REFERENCES "GitSyncJobs" ("Id", "ProjectGitRepositoryId") ON DELETE RESTRICT,
        CONSTRAINT "FK_ProjectGitEvidenceSnapshotCoverage_ProjectGitEvidenceSnapsh~" FOREIGN KEY ("SnapshotId", "ProjectId") REFERENCES "ProjectGitEvidenceSnapshots" ("Id", "ProjectId") ON DELETE RESTRICT,
        CONSTRAINT "FK_ProjectGitEvidenceSnapshotCoverage_ProjectGitRepositories_P~" FOREIGN KEY ("ProjectGitRepositoryId", "ProjectId") REFERENCES "ProjectGitRepositories" ("Id", "ProjectId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE INDEX "IX_ProjectGitEvidenceSnapshotCoverage_GitSyncCheckpointId_GitS~" ON "ProjectGitEvidenceSnapshotCoverage" ("GitSyncCheckpointId", "GitSyncJobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE INDEX "IX_ProjectGitEvidenceSnapshotCoverage_GitSyncJobId_ProjectGitR~" ON "ProjectGitEvidenceSnapshotCoverage" ("GitSyncJobId", "ProjectGitRepositoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE INDEX "IX_ProjectGitEvidenceSnapshotCoverage_ProjectGitRepositoryId_P~" ON "ProjectGitEvidenceSnapshotCoverage" ("ProjectGitRepositoryId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE UNIQUE INDEX "IX_ProjectGitEvidenceSnapshotCoverage_SnapshotId_GitSyncJobId_~" ON "ProjectGitEvidenceSnapshotCoverage" ("SnapshotId", "GitSyncJobId", "Category", "ScopeKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE INDEX "IX_ProjectGitEvidenceSnapshotCoverage_SnapshotId_ProjectId" ON "ProjectGitEvidenceSnapshotCoverage" ("SnapshotId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    CREATE FUNCTION uninet_snapshot_coverage_validate() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF NOT EXISTS (
        SELECT 1 FROM "ProjectGitEvidenceSnapshots" s
        JOIN "ProjectGitRepositories" a ON a."ProjectId" = s."ProjectId"
        JOIN "GitSyncJobs" j ON j."ProjectGitRepositoryId" = a."Id" AND j."GitHubRepositoryId" = a."GitHubRepositoryId"
        JOIN "GitSyncCheckpoints" c ON c."SyncJobId" = j."Id"
        WHERE s."Id" = NEW."SnapshotId" AND s."ProjectId" = NEW."ProjectId"
          AND s."CapturePolicyVersion" = 2 AND NOT s."Finalized"
          AND a."Id" = NEW."ProjectGitRepositoryId" AND j."Id" = NEW."GitSyncJobId"
          AND c."Id" = NEW."GitSyncCheckpointId" AND j."Status" = 2 AND c."Status" = 2
          AND c."Category" = NEW."Category" AND c."ScopeKey" = NEW."ScopeKey"
          AND c."ItemsProcessed" = NEW."ItemsProcessed" AND c."LastProcessedAt" = NEW."ScopeCompletedAt"
          AND j."CompletedAt" = NEW."JobCompletedAt" AND j."CompletedAt" <= s."ObservationCutoff"
      ) THEN
        RAISE EXCEPTION 'snapshot coverage requires matching completed retrieval state' USING ERRCODE = '23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER snapshot_coverage_immutable BEFORE INSERT OR UPDATE OR DELETE ON "ProjectGitEvidenceSnapshotCoverage"
      FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_entry_guard();
    CREATE TRIGGER snapshot_coverage_validate BEFORE INSERT ON "ProjectGitEvidenceSnapshotCoverage"
      FOR EACH ROW EXECUTE FUNCTION uninet_snapshot_coverage_validate();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119164008_AddProjectGitSnapshotCoverageReceipts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261119164008_AddProjectGitSnapshotCoverageReceipts', '8.0.11');
    END IF;
END $EF$;
COMMIT;

