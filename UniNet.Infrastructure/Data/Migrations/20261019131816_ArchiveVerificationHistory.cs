using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations;

public partial class ArchiveVerificationHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
SET LOCAL timezone = 'UTC';
SET LOCAL lock_timeout = '15s';
LOCK TABLE "VerificationAdminAction", "VerificationReportVersions", "VerificationReports", "ProjectVerifications",
    "VerificationAssignments", "RefundTransactions", "VerificationTransactions" IN ACCESS EXCLUSIVE MODE;

-- Reconcile D/E without rewriting report content, case results or refund actors.
DO $reconcile$
BEGIN
    IF EXISTS (
        SELECT 1 FROM "VerificationReports" r
        JOIN LATERAL (SELECT * FROM "VerificationReportVersions" WHERE "ReportId" = r."Id" ORDER BY "Version" DESC LIMIT 1) v ON true
        WHERE r."LegacyOriginalJson" IS NULL OR r."LegacyOriginalJson"->>'Id' IS DISTINCT FROM r."Id"::text
            OR r."Summary" IS DISTINCT FROM v."Summary" OR r."Findings" IS DISTINCT FROM v."Findings"
            OR r."Recommendations" IS DISTINCT FROM v."Recommendations" OR r."Outcome" IS DISTINCT FROM v."Outcome"
            OR r."SubmittedByUserId" IS DISTINCT FROM v."SubmittedByUserId" OR r."SubmittetAt" IS DISTINCT FROM v."SubmittedAt"
    ) THEN RAISE EXCEPTION 'G2 report canonical/revision/original snapshot mismatch; reconcile D/E before archiving'; END IF;

    IF EXISTS (
        SELECT 1 FROM (
            SELECT p."Id" case_id, p."Status", p."FinalizedAt", p."FinalizedByUserId", r."Outcome", r."SubmittedByUserId", v."Id" revision_id,
                row_number() OVER (PARTITION BY p."Id" ORDER BY COALESCE(v."SubmittedAt", r."SubmittetAt") DESC, r."Id") rn
            FROM "VerificationReports" r JOIN "VerificationAssignments" a ON a."Id" = r."AssignmentId"
            JOIN "ProjectVerifications" p ON p."Id" = a."ProjectVerificationId"
            LEFT JOIN LATERAL (SELECT * FROM "VerificationReportVersions" WHERE "ReportId" = r."Id" ORDER BY "Version" DESC LIMIT 1) v ON true
        ) h WHERE rn = 1 AND "FinalizedAt" IS NOT NULL AND "Status" IN (1,2) AND revision_id IS NOT NULL
            AND ("Outcome" IS DISTINCT FROM "Status" OR ("FinalizedByUserId" IS NOT NULL AND "FinalizedByUserId" IS DISTINCT FROM "SubmittedByUserId"
                AND NOT EXISTS (SELECT 1 FROM "VerificationAdminAction" a WHERE a."VerificationId" = h.case_id
                    AND a."ActorUserId" = h."FinalizedByUserId" AND a."Decision" = 'Uphold' AND a."CreatedAt" = h."FinalizedAt")))
    ) THEN RAISE EXCEPTION 'G2 finalized case outcome/finalizer disagrees with canonical report'; END IF;

    IF EXISTS (
        WITH actor AS (
            SELECT DISTINCT ON (a."RefundId") a."RefundId", a."ActorUserId", a."CreatedAt"
            FROM "VerificationAdminAction" a JOIN "RefundTransactions" r ON r."Id" = a."RefundId"
            JOIN "VerificationTransactions" p ON p."TransactionId" = r."TransactionId" AND p."VerificationId" = a."VerificationId"
            WHERE a."RefundId" IS NOT NULL AND a."Decision" = 'RefundRequested'
                AND NOT EXISTS (SELECT 1 FROM "VerificationAdminAction" other WHERE other."RefundId" = a."RefundId"
                    AND other."Decision" = 'RefundRequested' AND other."ActorUserId" <> a."ActorUserId")
            ORDER BY a."RefundId", a."CreatedAt", a."Id"
        ) SELECT 1 FROM actor a JOIN "RefundTransactions" r ON r."Id" = a."RefundId"
            WHERE r."RequestSource" <> 2 OR r."RequestedByUserId" IS DISTINCT FROM a."ActorUserId" OR r."RequestedAt" IS DISTINCT FROM a."CreatedAt"
    ) THEN RAISE EXCEPTION 'G2 refund audit disagrees with unambiguous legacy action; no actor was inferred or rewritten'; END IF;
END $reconcile$;

DO $archive$
DECLARE
    source_schema text;
    archive_schema text;
    table_name text;
    before_fingerprint jsonb;
    after_fingerprint jsonb;
    original_oid oid;
    moved_oid oid;
    fingerprint_sql text := 'SELECT jsonb_build_object(''count'',count(*),''keys'',coalesce(jsonb_agg("Id" ORDER BY "Id"),''[]''::jsonb),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex'')) FROM %I.%I t';
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.oid = '"VerificationReports"'::regclass;
    IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.oid IN ('"VerificationAdminAction"'::regclass, '"VerificationReportVersions"'::regclass) AND n.nspname <> source_schema)
    THEN RAISE EXCEPTION 'G2 historical tables must belong to the application schema'; END IF;
    archive_schema := CASE WHEN source_schema = 'public' THEN 'verification_archive' ELSE 'verification_archive_' || md5(source_schema) END;
    IF to_regnamespace(archive_schema) IS NOT NULL THEN
        RAISE EXCEPTION 'G2 archive schema % already exists; stop and inspect the existing archive', archive_schema;
    END IF;
    EXECUTE format('CREATE SCHEMA %I', archive_schema);
    EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC', archive_schema);
    EXECUTE format('CREATE TABLE %I."VerificationHistoryManifest" ("TableName" text PRIMARY KEY, "SourceSchema" text NOT NULL, "Fingerprint" jsonb NOT NULL, "ArchivedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP)', archive_schema);
    EXECUTE format($function$
        CREATE FUNCTION %I."RejectHistoryWrites"() RETURNS trigger LANGUAGE plpgsql AS $body$
        BEGIN RAISE EXCEPTION 'Verification history archive is read-only; restore through the G2 migration rollback'; END;
        $body$;
    $function$, archive_schema);
    FOREACH table_name IN ARRAY ARRAY['VerificationAdminAction', 'VerificationReportVersions'] LOOP
        EXECUTE format(fingerprint_sql, source_schema, table_name) INTO before_fingerprint;
        original_oid := to_regclass(format('%I.%I', source_schema, table_name));
        -- Move the original relation, retaining indexes, constraints, grants and inbound/outbound FKs.
        EXECUTE format('ALTER TABLE %I.%I SET SCHEMA %I', source_schema, table_name, archive_schema);
        moved_oid := to_regclass(format('%I.%I', archive_schema, table_name));
        EXECUTE format(fingerprint_sql, archive_schema, table_name) INTO after_fingerprint;
        IF original_oid IS DISTINCT FROM moved_oid OR before_fingerprint IS DISTINCT FROM after_fingerprint THEN
            RAISE EXCEPTION 'G2 archive relation/count/key/checksum mismatch for %', table_name;
        END IF;
        EXECUTE format('INSERT INTO %I."VerificationHistoryManifest" ("TableName","SourceSchema","Fingerprint") VALUES ($1,$2,$3)', archive_schema)
            USING table_name, source_schema, before_fingerprint;
        EXECUTE format('CREATE TRIGGER "G2_HistoryReadOnly" BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I.%I FOR EACH STATEMENT EXECUTE FUNCTION %I."RejectHistoryWrites"()', archive_schema, table_name, archive_schema);
    END LOOP;
    EXECUTE format('CREATE TRIGGER "G2_HistoryReadOnly" BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I."VerificationHistoryManifest" FOR EACH STATEMENT EXECUTE FUNCTION %I."RejectHistoryWrites"()', archive_schema, archive_schema);
END $archive$;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
SET LOCAL timezone = 'UTC';
SET LOCAL lock_timeout = '15s';
LOCK TABLE "VerificationReports", "ProjectVerifications", "VerificationAssignments", "RefundTransactions", "VerificationTransactions" IN ACCESS EXCLUSIVE MODE;
DO $restore$
DECLARE
    source_schema text;
    archive_schema text;
    table_name text;
    expected jsonb;
    actual jsonb;
    manifest_rows bigint;
    fingerprint_sql text := 'SELECT jsonb_build_object(''count'',count(*),''keys'',coalesce(jsonb_agg("Id" ORDER BY "Id"),''[]''::jsonb),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex'')) FROM %I.%I t';
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.oid = '"VerificationReports"'::regclass;
    archive_schema := CASE WHEN source_schema = 'public' THEN 'verification_archive' ELSE 'verification_archive_' || md5(source_schema) END;
    IF to_regclass(format('%I."VerificationHistoryManifest"', archive_schema)) IS NULL THEN
        RAISE EXCEPTION 'G2 archive manifest missing; downgrade refused. Restore a verified full database backup';
    END IF;
    EXECUTE format('LOCK TABLE %I."VerificationHistoryManifest" IN ACCESS EXCLUSIVE MODE', archive_schema);
    EXECUTE format('SELECT count(*) FROM %I."VerificationHistoryManifest"', archive_schema) INTO manifest_rows;
    IF manifest_rows <> 2 THEN RAISE EXCEPTION 'G2 archive manifest incomplete; downgrade refused'; END IF;
    FOREACH table_name IN ARRAY ARRAY['VerificationAdminAction', 'VerificationReportVersions'] LOOP
        IF to_regclass(format('%I.%I', source_schema, table_name)) IS NOT NULL THEN
            RAISE EXCEPTION 'G2 active history table % already exists; downgrade refused to overwrite data', table_name;
        END IF;
        IF to_regclass(format('%I.%I', archive_schema, table_name)) IS NULL THEN
            RAISE EXCEPTION 'G2 archived table % missing; restore a verified full database backup', table_name;
        END IF;
        EXECUTE format('LOCK TABLE %I.%I IN ACCESS EXCLUSIVE MODE', archive_schema, table_name);
        EXECUTE format('SELECT "Fingerprint" FROM %I."VerificationHistoryManifest" WHERE "TableName" = $1 AND "SourceSchema" = $2', archive_schema)
            INTO expected USING table_name, source_schema;
        EXECUTE format(fingerprint_sql, archive_schema, table_name) INTO actual;
        IF expected IS NULL OR expected IS DISTINCT FROM actual THEN
            RAISE EXCEPTION 'G2 archive count/key/checksum mismatch for %; downgrade refused', table_name;
        END IF;
        EXECUTE format('DROP TRIGGER "G2_HistoryReadOnly" ON %I.%I', archive_schema, table_name);
        EXECUTE format('ALTER TABLE %I.%I SET SCHEMA %I', archive_schema, table_name, source_schema);
        EXECUTE format(fingerprint_sql, source_schema, table_name) INTO actual;
        IF expected IS DISTINCT FROM actual THEN RAISE EXCEPTION 'G2 restored history mismatch for %', table_name; END IF;
    END LOOP;
    EXECUTE format('DROP TABLE %I."VerificationHistoryManifest"', archive_schema);
    EXECUTE format('DROP FUNCTION %I."RejectHistoryWrites"()', archive_schema);
    -- No CASCADE: unexpected archive objects must be reviewed, never silently deleted.
    EXECUTE format('DROP SCHEMA %I', archive_schema);
END $restore$;
""");
    }
}
