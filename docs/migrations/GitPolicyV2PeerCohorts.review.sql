START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    DROP INDEX "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" DROP CONSTRAINT "CK_GitContributionRuns_Policy";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD "CalculationContextHash" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD "CalculationStatus" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD "NormalizationPolicyVersion" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD "NormalizedResultsJson" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD "PeerSnapshotId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE TABLE "ExperimentalPeerCohorts" (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "RosterJson" jsonb NOT NULL,
        "RosterHash" character varying(64) NOT NULL,
        "Status" smallint NOT NULL,
        "OpenedById" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ClosedAt" timestamp with time zone,
        CONSTRAINT "PK_ExperimentalPeerCohorts" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ExperimentalPeerCohorts_Id_ProjectId" UNIQUE ("Id", "ProjectId"),
        CONSTRAINT "CK_ExperimentalPeerCohorts_Closed" CHECK (("Status" = 3) = ("ClosedAt" IS NOT NULL)),
        CONSTRAINT "CK_ExperimentalPeerCohorts_Roster" CHECK (jsonb_typeof("RosterJson") = 'array' AND jsonb_array_length("RosterJson") > 0 AND length("RosterHash") = 64),
        CONSTRAINT "CK_ExperimentalPeerCohorts_Status" CHECK ("Status" BETWEEN 0 AND 3),
        CONSTRAINT "FK_ExperimentalPeerCohorts_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ExperimentalPeerCohorts_Users_OpenedById" FOREIGN KEY ("OpenedById") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE TABLE "ExperimentalPeerSnapshots" (
        "Id" uuid NOT NULL,
        "ProjectId" uuid NOT NULL,
        "CohortId" uuid NOT NULL,
        "PolicyVersion" character varying(64) NOT NULL,
        "InputJson" jsonb NOT NULL,
        "InputHash" character varying(64) NOT NULL,
        "ScoresJson" jsonb NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ExperimentalPeerSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_ExperimentalPeerSnapshots_Id_ProjectId" UNIQUE ("Id", "ProjectId"),
        CONSTRAINT "CK_ExperimentalPeerSnapshots_Hash" CHECK (length("InputHash") = 64),
        CONSTRAINT "FK_ExperimentalPeerSnapshots_ExperimentalPeerCohorts_CohortId_~" FOREIGN KEY ("CohortId", "ProjectId") REFERENCES "ExperimentalPeerCohorts" ("Id", "ProjectId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE TABLE "ExperimentalPeerSubmissions" (
        "Id" uuid NOT NULL,
        "CohortId" uuid NOT NULL,
        "EvaluatorId" uuid NOT NULL,
        "EvaluateeId" uuid NOT NULL,
        "Score" integer NOT NULL,
        "Comment" character varying(2000),
        "Revision" integer NOT NULL,
        "SubmittedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ExperimentalPeerSubmissions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ExperimentalPeerSubmissions_Score" CHECK ("Score" BETWEEN 1 AND 5 AND "Revision" > 0 AND "EvaluatorId" <> "EvaluateeId"),
        CONSTRAINT "FK_ExperimentalPeerSubmissions_ExperimentalPeerCohorts_CohortId" FOREIGN KEY ("CohortId") REFERENCES "ExperimentalPeerCohorts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ExperimentalPeerSubmissions_Users_EvaluateeId" FOREIGN KEY ("EvaluateeId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ExperimentalPeerSubmissions_Users_EvaluatorId" FOREIGN KEY ("EvaluatorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE TABLE "ExperimentalPeerRevisions" (
        "Id" uuid NOT NULL,
        "SubmissionId" uuid NOT NULL,
        "Revision" integer NOT NULL,
        "PreviousScore" integer,
        "Score" integer NOT NULL,
        "PreviousComment" character varying(2000),
        "Comment" character varying(2000),
        "ChangedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ExperimentalPeerRevisions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_ExperimentalPeerRevisions_Score" CHECK ("Score" BETWEEN 1 AND 5 AND ("PreviousScore" IS NULL OR "PreviousScore" BETWEEN 1 AND 5) AND "Revision" > 0),
        CONSTRAINT "FK_ExperimentalPeerRevisions_ExperimentalPeerSubmissions_Submi~" FOREIGN KEY ("SubmissionId") REFERENCES "ExperimentalPeerSubmissions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE INDEX "IX_GitContributionRuns_PeerSnapshotId_ProjectId" ON "GitContributionRuns" ("PeerSnapshotId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE UNIQUE INDEX "IX_GitContributionRuns_ProjectId_CalculationContextHash" ON "GitContributionRuns" ("ProjectId", "CalculationContextHash") WHERE "CalculationContextHash" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE UNIQUE INDEX "IX_GitContributionRuns_ProjectId_GitEvidenceSnapshotId_Scoring~" ON "GitContributionRuns" ("ProjectId", "GitEvidenceSnapshotId", "ScoringPolicyVersion") WHERE "ScoringPolicyVersion" = 'ExperimentalGitScoringV1';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD CONSTRAINT "CK_GitContributionRuns_Policy" CHECK ("ScoringPolicyVersion" IN ('ExperimentalGitScoringV1','ExperimentalGitScoringV2'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD CONSTRAINT "CK_GitContributionRuns_V2Context" CHECK ("ScoringPolicyVersion" <> 'ExperimentalGitScoringV2' OR length("CalculationContextHash") = 64 AND "CalculationContextHash" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE INDEX "IX_ExperimentalPeerCohorts_OpenedById" ON "ExperimentalPeerCohorts" ("OpenedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE INDEX "IX_ExperimentalPeerCohorts_ProjectId" ON "ExperimentalPeerCohorts" ("ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE UNIQUE INDEX "IX_ExperimentalPeerRevisions_SubmissionId_Revision" ON "ExperimentalPeerRevisions" ("SubmissionId", "Revision");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE UNIQUE INDEX "IX_ExperimentalPeerSnapshots_CohortId" ON "ExperimentalPeerSnapshots" ("CohortId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE INDEX "IX_ExperimentalPeerSnapshots_CohortId_ProjectId" ON "ExperimentalPeerSnapshots" ("CohortId", "ProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE UNIQUE INDEX "IX_ExperimentalPeerSubmissions_CohortId_EvaluatorId_EvaluateeId" ON "ExperimentalPeerSubmissions" ("CohortId", "EvaluatorId", "EvaluateeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE INDEX "IX_ExperimentalPeerSubmissions_EvaluateeId" ON "ExperimentalPeerSubmissions" ("EvaluateeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE INDEX "IX_ExperimentalPeerSubmissions_EvaluatorId" ON "ExperimentalPeerSubmissions" ("EvaluatorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    ALTER TABLE "GitContributionRuns" ADD CONSTRAINT "FK_GitContributionRuns_ExperimentalPeerSnapshots_PeerSnapshotI~" FOREIGN KEY ("PeerSnapshotId", "ProjectId") REFERENCES "ExperimentalPeerSnapshots" ("Id", "ProjectId") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    CREATE FUNCTION uninet_peer_cohort_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE required_count integer; actual_count integer;
    BEGIN
      IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Peer audit cannot be deleted' USING ERRCODE='55000'; END IF;
      IF TG_OP = 'INSERT' THEN
        IF NEW."Status" = 3 THEN RAISE EXCEPTION 'Cohort must begin open' USING ERRCODE='55000'; END IF;
        IF (SELECT count(DISTINCT value) FROM jsonb_array_elements_text(NEW."RosterJson")) <> jsonb_array_length(NEW."RosterJson") THEN
          RAISE EXCEPTION 'Duplicate cohort participant' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END IF;
      IF OLD."Status"=3 OR (NEW."Id",NEW."ProjectId",NEW."RosterJson",NEW."RosterHash",NEW."CreatedAt",NEW."OpenedById") IS DISTINCT FROM
        (OLD."Id",OLD."ProjectId",OLD."RosterJson",OLD."RosterHash",OLD."CreatedAt",OLD."OpenedById") THEN
        RAISE EXCEPTION 'Cohort roster and closed cohort are immutable' USING ERRCODE='55000';
      END IF;
      IF NEW."Status"=3 THEN
        required_count := jsonb_array_length(OLD."RosterJson") * (jsonb_array_length(OLD."RosterJson")-1);
        SELECT count(*) INTO actual_count FROM "ExperimentalPeerSubmissions" WHERE "CohortId"=OLD."Id";
        IF actual_count<>required_count OR NOT EXISTS(SELECT 1 FROM "ExperimentalPeerSnapshots" WHERE "CohortId"=OLD."Id") THEN
          RAISE EXCEPTION 'Complete ratings and peer snapshot required' USING ERRCODE='23514';
        END IF;
      END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER peer_cohort_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExperimentalPeerCohorts" FOR EACH ROW EXECUTE FUNCTION uninet_peer_cohort_guard();

    CREATE FUNCTION uninet_peer_submission_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE c "ExperimentalPeerCohorts"%ROWTYPE;
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Peer audit cannot be deleted' USING ERRCODE='55000'; END IF;
      SELECT * INTO c FROM "ExperimentalPeerCohorts" WHERE "Id"=NEW."CohortId" FOR UPDATE;
      IF NOT FOUND OR c."Status"=3 THEN RAISE EXCEPTION 'Peer cohort is closed or missing' USING ERRCODE='55000'; END IF;
      IF NOT (c."RosterJson" ? NEW."EvaluatorId"::text) OR NOT (c."RosterJson" ? NEW."EvaluateeId"::text) THEN
        RAISE EXCEPTION 'Peer users must be cohort participants' USING ERRCODE='23514';
      END IF;
      IF TG_OP='UPDATE' AND ((NEW."Id",NEW."CohortId",NEW."EvaluatorId",NEW."EvaluateeId",NEW."SubmittedAt") IS DISTINCT FROM
        (OLD."Id",OLD."CohortId",OLD."EvaluatorId",OLD."EvaluateeId",OLD."SubmittedAt") OR NEW."Revision"<>OLD."Revision"+1) THEN
        RAISE EXCEPTION 'Peer identity or revision invalid' USING ERRCODE='23514';
      END IF;
      IF TG_OP='INSERT' AND NEW."Revision"<>1 THEN RAISE EXCEPTION 'Initial peer revision must be one' USING ERRCODE='23514'; END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER peer_submission_guard BEFORE INSERT OR UPDATE OR DELETE ON "ExperimentalPeerSubmissions" FOR EACH ROW EXECUTE FUNCTION uninet_peer_submission_guard();
    CREATE FUNCTION uninet_peer_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN RAISE EXCEPTION 'Peer snapshot/history is immutable' USING ERRCODE='55000'; END $$;
    CREATE TRIGGER peer_snapshot_immutable BEFORE UPDATE OR DELETE ON "ExperimentalPeerSnapshots" FOR EACH ROW EXECUTE FUNCTION uninet_peer_append_only();
    CREATE TRIGGER peer_history_immutable BEFORE UPDATE OR DELETE ON "ExperimentalPeerRevisions" FOR EACH ROW EXECUTE FUNCTION uninet_peer_append_only();
    CREATE FUNCTION uninet_peer_history_insert_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE current_revision integer; cohort_status smallint;
    BEGIN
      SELECT s."Revision",c."Status" INTO current_revision,cohort_status FROM "ExperimentalPeerSubmissions" s JOIN "ExperimentalPeerCohorts" c ON c."Id"=s."CohortId"
        WHERE s."Id"=NEW."SubmissionId" FOR UPDATE OF c;
      IF NOT FOUND OR cohort_status=3 OR current_revision<>NEW."Revision" THEN RAISE EXCEPTION 'Peer history revision invalid or cohort closed' USING ERRCODE='55000'; END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER peer_history_insert_guard BEFORE INSERT ON "ExperimentalPeerRevisions" FOR EACH ROW EXECUTE FUNCTION uninet_peer_history_insert_guard();
    CREATE FUNCTION uninet_peer_snapshot_insert_guard() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE c "ExperimentalPeerCohorts"%ROWTYPE; actual_count integer;
    BEGIN
      SELECT * INTO c FROM "ExperimentalPeerCohorts" WHERE "Id"=NEW."CohortId" FOR UPDATE;
      IF NOT FOUND OR c."Status"<>2 OR c."ProjectId"<>NEW."ProjectId" THEN RAISE EXCEPTION 'Peer cohort is not ready' USING ERRCODE='23514'; END IF;
      SELECT count(*) INTO actual_count FROM "ExperimentalPeerSubmissions" WHERE "CohortId"=c."Id";
      IF actual_count<>jsonb_array_length(c."RosterJson")*(jsonb_array_length(c."RosterJson")-1) THEN RAISE EXCEPTION 'Peer ratings incomplete' USING ERRCODE='23514'; END IF;
      RETURN NEW;
    END $$;
    CREATE TRIGGER peer_snapshot_insert_guard BEFORE INSERT ON "ExperimentalPeerSnapshots" FOR EACH ROW EXECUTE FUNCTION uninet_peer_snapshot_insert_guard();
    CREATE FUNCTION uninet_peer_snapshot_must_close() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF NOT EXISTS(SELECT 1 FROM "ExperimentalPeerCohorts" WHERE "Id"=NEW."CohortId" AND "Status"=3) THEN
        RAISE EXCEPTION 'Peer snapshot requires atomic cohort close' USING ERRCODE='23514';
      END IF; RETURN NULL;
    END $$;
    CREATE CONSTRAINT TRIGGER peer_snapshot_must_close AFTER INSERT ON "ExperimentalPeerSnapshots" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION uninet_peer_snapshot_must_close();
    CREATE FUNCTION uninet_git_v2_inputs_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD."ScoringPolicyVersion"='ExperimentalGitScoringV2' AND
        (NEW."ProjectId",NEW."GitEvidenceSnapshotId",NEW."ScoringPolicyVersion",NEW."InputSnapshotJson",NEW."InputHash",NEW."CategoryStatusJson",NEW."ResultsJson",NEW."ExcludedActivitiesJson",NEW."PeerSnapshotId",NEW."CalculationContextHash",NEW."NormalizationPolicyVersion",NEW."CalculationStatus",NEW."NormalizedResultsJson") IS DISTINCT FROM
        (OLD."ProjectId",OLD."GitEvidenceSnapshotId",OLD."ScoringPolicyVersion",OLD."InputSnapshotJson",OLD."InputHash",OLD."CategoryStatusJson",OLD."ResultsJson",OLD."ExcludedActivitiesJson",OLD."PeerSnapshotId",OLD."CalculationContextHash",OLD."NormalizationPolicyVersion",OLD."CalculationStatus",OLD."NormalizedResultsJson") THEN
        RAISE EXCEPTION 'V2 calculation inputs/results are immutable; create a new run' USING ERRCODE='55000';
      END IF; RETURN NEW;
    END $$;
    CREATE TRIGGER git_v2_inputs_immutable BEFORE UPDATE ON "GitContributionRuns" FOR EACH ROW EXECUTE FUNCTION uninet_git_v2_inputs_immutable();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118152617_GuardGitV2FinalNormalizationReadiness') THEN
    ALTER TABLE "GitContributionRuns" ADD CONSTRAINT "CK_GitContributionRuns_V2FinalReady" CHECK ("ScoringPolicyVersion" <> 'ExperimentalGitScoringV2' OR "Status" <> 3 OR ("CalculationStatus" = 'Ready' AND "CalculationStatus" IS NOT NULL AND "NormalizedResultsJson" IS NOT NULL AND "NormalizationPolicyVersion" IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118152617_GuardGitV2FinalNormalizationReadiness') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261118152617_GuardGitV2FinalNormalizationReadiness', '8.0.11');
    END IF;
END $EF$;
COMMIT;

