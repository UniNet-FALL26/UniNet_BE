START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    ALTER TABLE "GitContributionRuns" ADD CONSTRAINT "AK_GitContributionRuns_Id_ProjectId" UNIQUE ("Id", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE TABLE "ProjectContributionWorkflows" (
        "ProjectId" uuid NOT NULL,
        "Mode" character varying(32) NOT NULL,
        "SelectedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ProjectContributionWorkflows" PRIMARY KEY ("ProjectId"),
        CONSTRAINT "CK_OfficialWorkflow_Mode" CHECK ("Mode" IN ('OfficialV2','LegacyV1','RequiresOptIn')),
        CONSTRAINT "FK_ProjectContributionWorkflows_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE TABLE "OfficialContributionRuns" (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "State" character varying(32) NOT NULL,
        "Revision" integer NOT NULL,
        "ResultHash" character varying(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedById" uuid NOT NULL,
        "FinalizedAt" timestamp with time zone,
        "FinalizedById" uuid,
        CONSTRAINT "PK_OfficialContributionRuns" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_OfficialContributionRuns_Id_ProjectId" UNIQUE ("Id", "ProjectId"),
        CONSTRAINT "CK_OfficialRun_Finalized" CHECK (("State" = 'Finalized') = ("FinalizedAt" IS NOT NULL AND "FinalizedById" IS NOT NULL)),
        CONSTRAINT "CK_OfficialRun_Revision" CHECK ("Revision" >= 0 AND length("ResultHash") = 64),
        CONSTRAINT "CK_OfficialRun_State" CHECK ("State" IN ('Draft','Review','Approved','Rejected','Finalized')),
        CONSTRAINT "FK_OfficialContributionRuns_GitContributionRuns_Id_ProjectId" FOREIGN KEY ("Id", "ProjectId") REFERENCES "GitContributionRuns" ("Id", "ProjectId") ON DELETE RESTRICT,
        CONSTRAINT "FK_OfficialContributionRuns_ProjectContributionWorkflows_Proje~" FOREIGN KEY ("ProjectId") REFERENCES "ProjectContributionWorkflows" ("ProjectId") ON DELETE RESTRICT,
        CONSTRAINT "FK_OfficialContributionRuns_Users_CreatedById" FOREIGN KEY ("CreatedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_OfficialContributionRuns_Users_FinalizedById" FOREIGN KEY ("FinalizedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE TABLE "OfficialContributionAudits" (
        "Id" uuid NOT NULL,
        "RunId" uuid NOT NULL,
        "Revision" integer NOT NULL,
        "Action" character varying(32) NOT NULL,
        "ActorId" uuid NOT NULL,
        "At" timestamp with time zone NOT NULL,
        "Feedback" character varying(2000),
        CONSTRAINT "PK_OfficialContributionAudits" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OfficialContributionAudits_OfficialContributionRuns_RunId" FOREIGN KEY ("RunId") REFERENCES "OfficialContributionRuns" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_OfficialContributionAudits_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE TABLE "OfficialContributionPublications" (
        "ProjectId" uuid NOT NULL,
        "RunId" uuid NOT NULL,
        "PublishedById" uuid NOT NULL,
        "PublishedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OfficialContributionPublications" PRIMARY KEY ("ProjectId"),
        CONSTRAINT "FK_OfficialContributionPublications_OfficialContributionRuns_R~" FOREIGN KEY ("RunId", "ProjectId") REFERENCES "OfficialContributionRuns" ("Id", "ProjectId") ON DELETE RESTRICT,
        CONSTRAINT "FK_OfficialContributionPublications_Users_PublishedById" FOREIGN KEY ("PublishedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE INDEX "IX_OfficialContributionAudits_ActorId" ON "OfficialContributionAudits" ("ActorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionAudits_RunId_Revision" ON "OfficialContributionAudits" ("RunId", "Revision");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE INDEX "IX_OfficialContributionPublications_PublishedById" ON "OfficialContributionPublications" ("PublishedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionPublications_RunId" ON "OfficialContributionPublications" ("RunId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionPublications_RunId_ProjectId" ON "OfficialContributionPublications" ("RunId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE INDEX "IX_OfficialContributionRuns_CreatedById" ON "OfficialContributionRuns" ("CreatedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE INDEX "IX_OfficialContributionRuns_FinalizedById" ON "OfficialContributionRuns" ("FinalizedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionRuns_ProjectId" ON "OfficialContributionRuns" ("ProjectId") WHERE "State" = 'Finalized';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionRuns_ProjectId_RequestId" ON "OfficialContributionRuns" ("ProjectId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    INSERT INTO "ProjectContributionWorkflows" ("ProjectId", "Mode", "SelectedAt")
    SELECT p."Id", CASE WHEN EXISTS(SELECT 1 FROM "ContributionCycles" c WHERE c."ProjectId"=p."Id" AND c."Status"=2)
      THEN 'LegacyV1' ELSE 'RequiresOptIn' END, CURRENT_TIMESTAMP FROM "Projects" p;
    CREATE FUNCTION uninet_official_default_workflow() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      INSERT INTO "ProjectContributionWorkflows" ("ProjectId","Mode","SelectedAt") VALUES(NEW."Id",'OfficialV2',CURRENT_TIMESTAMP);
      RETURN NEW;
    END $$;
    CREATE TRIGGER official_project_workflow AFTER INSERT ON "Projects" FOR EACH ROW EXECUTE FUNCTION uninet_official_default_workflow();
    CREATE FUNCTION uninet_official_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN RAISE EXCEPTION 'official audit data is immutable' USING ERRCODE='55000'; END $$;
    CREATE FUNCTION uninet_official_workflow_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF TG_OP='DELETE' AND NOT EXISTS(SELECT 1 FROM "Projects" WHERE "Id"=OLD."ProjectId")
        AND NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "ProjectId"=OLD."ProjectId") THEN RETURN OLD; END IF;
      RAISE EXCEPTION 'workflow selection is immutable' USING ERRCODE='55000';
    END $$;
    CREATE TRIGGER official_workflow_immutable BEFORE UPDATE OR DELETE ON "ProjectContributionWorkflows" FOR EACH ROW EXECUTE FUNCTION uninet_official_workflow_guard();
    CREATE TRIGGER official_audit_immutable BEFORE UPDATE OR DELETE ON "OfficialContributionAudits" FOR EACH ROW EXECUTE FUNCTION uninet_official_append_only();
    CREATE TRIGGER official_publication_immutable BEFORE UPDATE OR DELETE ON "OfficialContributionPublications" FOR EACH ROW EXECUTE FUNCTION uninet_official_append_only();
    CREATE FUNCTION uninet_official_calculation_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "Id"=OLD."Id") THEN
        RAISE EXCEPTION 'official calculation is frozen' USING ERRCODE='55000';
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
    END $$;
    CREATE TRIGGER official_calculation_immutable BEFORE UPDATE OR DELETE ON "GitContributionRuns" FOR EACH ROW EXECUTE FUNCTION uninet_official_calculation_guard();
    CREATE FUNCTION uninet_official_run_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'official runs retain audit history' USING ERRCODE='55000'; END IF;
      IF TG_OP='INSERT' THEN
        PERFORM 1 FROM "GitContributionRuns" WHERE "Id"=NEW."Id" AND "ProjectId"=NEW."ProjectId" FOR UPDATE;
        IF NEW."State"<>'Draft' OR NEW."Revision"<>0 OR
          NOT EXISTS(SELECT 1 FROM "ProjectContributionWorkflows" WHERE "ProjectId"=NEW."ProjectId" AND "Mode"='OfficialV2') OR
          NOT EXISTS(SELECT 1 FROM "GitContributionRuns" WHERE "Id"=NEW."Id" AND "ProjectId"=NEW."ProjectId"
            AND "ScoringPolicyVersion"='ExperimentalGitScoringV2' AND "CalculationStatus"='Ready'
            AND "PeerSnapshotId" IS NOT NULL AND "NormalizationPolicyVersion"='GitPeerSeparate60_40V1') THEN
          RAISE EXCEPTION 'official ready V2 calculation required' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END IF;
      IF OLD."State"='Finalized' OR (NEW."Id",NEW."ProjectId",NEW."RequestId",NEW."ResultHash",NEW."CreatedAt",NEW."CreatedById")
         IS DISTINCT FROM (OLD."Id",OLD."ProjectId",OLD."RequestId",OLD."ResultHash",OLD."CreatedAt",OLD."CreatedById") THEN
        RAISE EXCEPTION 'official frozen result cannot change' USING ERRCODE='55000';
      END IF;
      IF NEW."Revision"<>OLD."Revision"+1 OR NOT (
        (OLD."State" IN ('Draft','Rejected') AND NEW."State"='Review') OR (OLD."State"='Review' AND NEW."State" IN ('Approved','Rejected')) OR
        (OLD."State"='Approved' AND NEW."State"='Finalized')) THEN
        RAISE EXCEPTION 'invalid official lifecycle transition' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER official_run_integrity BEFORE INSERT OR UPDATE OR DELETE ON "OfficialContributionRuns" FOR EACH ROW EXECUTE FUNCTION uninet_official_run_guard();
    CREATE FUNCTION uninet_official_publication_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      PERFORM 1 FROM "OfficialContributionRuns" WHERE "Id"=NEW."RunId" AND "ProjectId"=NEW."ProjectId" FOR UPDATE;
      IF NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "Id"=NEW."RunId" AND "ProjectId"=NEW."ProjectId" AND "State"='Finalized') THEN
        RAISE EXCEPTION 'only finalized official results can be published' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER official_publish_integrity BEFORE INSERT ON "OfficialContributionPublications" FOR EACH ROW EXECUTE FUNCTION uninet_official_publication_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261119190820_AddOfficialContributionV2Workflow') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261119190820_AddOfficialContributionV2Workflow', '8.0.11');
    END IF;
END $EF$;
COMMIT;

