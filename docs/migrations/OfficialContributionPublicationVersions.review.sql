START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    DROP INDEX "IX_OfficialContributionRuns_ProjectId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    CREATE TABLE "OfficialContributionReleases" (
      "RunId" uuid NOT NULL CONSTRAINT "PK_OfficialContributionReleases" PRIMARY KEY,
      "ProjectId" uuid NOT NULL,
      "Version" integer NOT NULL,
      "PreviousRunId" uuid NULL,
      "PublishedById" uuid NOT NULL CONSTRAINT "FK_OfficialContributionReleases_Users_PublishedById" REFERENCES "Users"("Id") ON DELETE RESTRICT,
      "PublishedAt" timestamp with time zone NOT NULL,
      "Reason" varchar(2000) NULL,
      CONSTRAINT "CK_OfficialRelease_Version" CHECK ("Version">0),
      CONSTRAINT "FK_OfficialContributionReleases_OfficialContributionRuns_RunId~" FOREIGN KEY ("RunId","ProjectId") REFERENCES "OfficialContributionRuns"("Id","ProjectId") ON DELETE RESTRICT,
      CONSTRAINT "FK_OfficialContributionReleases_OfficialContributionRuns_Previ~" FOREIGN KEY ("PreviousRunId","ProjectId") REFERENCES "OfficialContributionRuns"("Id","ProjectId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    CREATE INDEX "IX_OfficialContributionRuns_ProjectId" ON "OfficialContributionRuns" ("ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    CREATE INDEX "IX_OfficialContributionReleases_PreviousRunId_ProjectId" ON "OfficialContributionReleases" ("PreviousRunId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionReleases_ProjectId_Version" ON "OfficialContributionReleases" ("ProjectId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    CREATE INDEX "IX_OfficialContributionReleases_PublishedById" ON "OfficialContributionReleases" ("PublishedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    CREATE UNIQUE INDEX "IX_OfficialContributionReleases_RunId_ProjectId" ON "OfficialContributionReleases" ("RunId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    -- Import the immutable first-publication receipts without modifying source rows.
    INSERT INTO "OfficialContributionReleases" ("RunId","ProjectId","Version","PreviousRunId","PublishedById","PublishedAt","Reason")
    SELECT "RunId","ProjectId",1,NULL,"PublishedById","PublishedAt",NULL FROM "OfficialContributionPublications";
    CREATE FUNCTION uninet_official_release_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE prior "OfficialContributionReleases"%ROWTYPE;
    BEGIN
      IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'publication history is immutable' USING ERRCODE='55000'; END IF;
      PERFORM 1 FROM "Projects" WHERE "Id"=NEW."ProjectId" FOR UPDATE;
      SELECT * INTO prior FROM "OfficialContributionReleases" WHERE "ProjectId"=NEW."ProjectId" ORDER BY "Version" DESC LIMIT 1;
      IF NEW."Version"<>COALESCE(prior."Version",0)+1 OR NEW."PreviousRunId" IS DISTINCT FROM prior."RunId" THEN
        RAISE EXCEPTION 'publication version changed' USING ERRCODE='23514';
      END IF;
      IF NOT EXISTS(SELECT 1 FROM "OfficialContributionRuns" WHERE "Id"=NEW."RunId" AND "ProjectId"=NEW."ProjectId" AND "State"='Finalized') THEN
        RAISE EXCEPTION 'finalized project result required' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER official_release_integrity BEFORE INSERT OR UPDATE OR DELETE ON "OfficialContributionReleases"
      FOR EACH ROW EXECUTE FUNCTION uninet_official_release_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261123061945_AddOfficialContributionPublicationVersions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261123061945_AddOfficialContributionPublicationVersions', '8.0.11');
    END IF;
END $EF$;
COMMIT;

