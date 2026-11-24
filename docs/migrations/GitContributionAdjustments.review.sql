START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE TABLE "GitContributionAdjustments" (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "CreatedById" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExecutionDeadline" timestamp with time zone NOT NULL,
        "GitSnapshotId" uuid,
        "PeerCohortId" uuid,
        "RunId" uuid,
        "BasePublishedRunId" uuid,
        "State" character varying(24) NOT NULL,
        "Revision" integer NOT NULL,
        CONSTRAINT "PK_GitContributionAdjustments" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_GitContributionAdjustments_Id_ProjectId" UNIQUE ("Id", "ProjectId"),
        CONSTRAINT "CK_GitAdjustment_State" CHECK ("State" IN ('Syncing','GitCaptured','PeerOpen','DraftCreated','Published','Failed')),
        CONSTRAINT "FK_GitContributionAdjustments_ExperimentalPeerCohorts_PeerCoho~" FOREIGN KEY ("PeerCohortId") REFERENCES "ExperimentalPeerCohorts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GitContributionAdjustments_OfficialContributionRuns_RunId" FOREIGN KEY ("RunId") REFERENCES "OfficialContributionRuns" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GitContributionAdjustments_ProjectGitEvidenceSnapshots_GitS~" FOREIGN KEY ("GitSnapshotId") REFERENCES "ProjectGitEvidenceSnapshots" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GitContributionAdjustments_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_GitContributionAdjustments_Users_CreatedById" FOREIGN KEY ("CreatedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE TABLE "GitContributionAdjustmentRepositories" (
        "AdjustmentId" uuid NOT NULL,
        "AssociationId" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "RepositoryId" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        CONSTRAINT "PK_GitContributionAdjustmentRepositories" PRIMARY KEY ("AdjustmentId", "AssociationId"),
        CONSTRAINT "FK_GitContributionAdjustmentRepositories_GitContributionAdjust~" FOREIGN KEY ("AdjustmentId", "ProjectId") REFERENCES "GitContributionAdjustments" ("Id", "ProjectId") ON DELETE RESTRICT,
        CONSTRAINT "FK_GitContributionAdjustmentRepositories_GitSyncJobs_JobId_Ass~" FOREIGN KEY ("JobId", "AssociationId", "RepositoryId") REFERENCES "GitSyncJobs" ("Id", "ProjectGitRepositoryId", "GitHubRepositoryId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE INDEX "IX_GitContributionAdjustmentRepositories_AdjustmentId_ProjectId" ON "GitContributionAdjustmentRepositories" ("AdjustmentId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE UNIQUE INDEX "IX_GitContributionAdjustmentRepositories_JobId" ON "GitContributionAdjustmentRepositories" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE INDEX "IX_GitContributionAdjustmentRepositories_JobId_AssociationId_R~" ON "GitContributionAdjustmentRepositories" ("JobId", "AssociationId", "RepositoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE INDEX "IX_GitContributionAdjustments_CreatedById" ON "GitContributionAdjustments" ("CreatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE INDEX "IX_GitContributionAdjustments_GitSnapshotId" ON "GitContributionAdjustments" ("GitSnapshotId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE INDEX "IX_GitContributionAdjustments_PeerCohortId" ON "GitContributionAdjustments" ("PeerCohortId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE UNIQUE INDEX "IX_GitContributionAdjustments_ProjectId" ON "GitContributionAdjustments" ("ProjectId") WHERE "State" NOT IN ('Published','Failed');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE UNIQUE INDEX "IX_GitContributionAdjustments_ProjectId_RequestId" ON "GitContributionAdjustments" ("ProjectId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE INDEX "IX_GitContributionAdjustments_RunId" ON "GitContributionAdjustments" ("RunId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    CREATE FUNCTION uninet_adjustment_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Adjustment audit is retained' USING ERRCODE='55000'; END IF;
      IF TG_TABLE_NAME='GitContributionAdjustmentRepositories' THEN
        IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Adjustment scope is immutable' USING ERRCODE='55000'; END IF;
        IF NOT EXISTS(SELECT 1 FROM "GitSyncJobs" j JOIN "ProjectGitRepositories" l ON l."Id"=j."ProjectGitRepositoryId"
          JOIN "GitContributionAdjustments" a ON a."Id"=NEW."AdjustmentId"
          WHERE j."Id"=NEW."JobId" AND l."Id"=NEW."AssociationId" AND l."ProjectId"=NEW."ProjectId"
            AND a."ProjectId"=NEW."ProjectId" AND j."SyncType"=3 AND j."CreatedAt">=a."CreatedAt") THEN
          RAISE EXCEPTION 'Invalid adjustment job correlation' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END IF;
      IF TG_OP='UPDATE' AND (
        ROW(NEW."Id",NEW."ProjectId",NEW."RequestId",NEW."CreatedById",NEW."CreatedAt",NEW."ExecutionDeadline",NEW."BasePublishedRunId")
          IS DISTINCT FROM ROW(OLD."Id",OLD."ProjectId",OLD."RequestId",OLD."CreatedById",OLD."CreatedAt",OLD."ExecutionDeadline",OLD."BasePublishedRunId")
        OR OLD."GitSnapshotId" IS NOT NULL AND NEW."GitSnapshotId" IS DISTINCT FROM OLD."GitSnapshotId"
        OR OLD."PeerCohortId" IS NOT NULL AND NEW."PeerCohortId" IS DISTINCT FROM OLD."PeerCohortId"
        OR OLD."RunId" IS NOT NULL AND NEW."RunId" IS DISTINCT FROM OLD."RunId"
        OR NEW."Revision"<>OLD."Revision"+1) THEN
          RAISE EXCEPTION 'Adjustment correlation is immutable' USING ERRCODE='55000';
      END IF;
      IF NEW."GitSnapshotId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ProjectGitEvidenceSnapshots" s WHERE s."Id"=NEW."GitSnapshotId" AND s."ProjectId"=NEW."ProjectId" AND s."Finalized" AND s."CapturePolicyVersion"=2 AND s."CreatedAt">=NEW."CreatedAt") THEN
        RAISE EXCEPTION 'Invalid adjustment snapshot' USING ERRCODE='23514';
      END IF;
      IF NEW."PeerCohortId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "ExperimentalPeerCohorts" c WHERE c."Id"=NEW."PeerCohortId" AND c."ProjectId"=NEW."ProjectId" AND c."CreatedAt">=NEW."CreatedAt") THEN
        RAISE EXCEPTION 'Invalid adjustment peer round' USING ERRCODE='23514';
      END IF;
      IF NEW."RunId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" r JOIN "GitContributionRuns" g ON g."Id"=r."Id" JOIN "ExperimentalPeerSnapshots" p ON p."Id"=g."PeerSnapshotId" WHERE r."Id"=NEW."RunId" AND r."ProjectId"=NEW."ProjectId" AND g."GitEvidenceSnapshotId"=NEW."GitSnapshotId" AND p."CohortId"=NEW."PeerCohortId") THEN
        RAISE EXCEPTION 'Invalid adjustment run' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER adjustment_guard BEFORE INSERT OR UPDATE OR DELETE ON "GitContributionAdjustments" FOR EACH ROW EXECUTE FUNCTION uninet_adjustment_guard();
    CREATE TRIGGER adjustment_scope_guard BEFORE INSERT OR UPDATE OR DELETE ON "GitContributionAdjustmentRepositories" FOR EACH ROW EXECUTE FUNCTION uninet_adjustment_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123071955_AddGitContributionAdjustments') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123071955_AddGitContributionAdjustments', '8.0.11');
    END IF;
END $EF$;
COMMIT;

