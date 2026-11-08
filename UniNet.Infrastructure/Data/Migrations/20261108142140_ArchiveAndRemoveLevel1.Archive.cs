namespace UniNet.Infrastructure.Data.Migrations;

public partial class ArchiveAndRemoveLevel1
{
    private const string ArchiveLevel1Sql = """
SET LOCAL timezone = 'UTC';
SET LOCAL lock_timeout = '15s';
LOCK TABLE "ProjectModerations", "EvidenceModerationAttempts", "ProjectVerifications" IN ACCESS EXCLUSIVE MODE;
DO $archive$
DECLARE
    source_schema text;
    archive_schema text;
    table_name text;
    expected jsonb;
    actual jsonb;
    unsafe boolean;
    fingerprint_sql text := 'SELECT jsonb_build_object(''count'',count(*),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex''),''columns'',(SELECT jsonb_agg(jsonb_build_array(attname,format_type(atttypid,atttypmod),attnotnull) ORDER BY attnum) FROM pg_attribute WHERE attrelid=%L::regclass AND attnum>0 AND NOT attisdropped)) FROM %I.%I t';
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE c.oid='"ProjectModerations"'::regclass;
    IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE c.oid IN ('"EvidenceModerationAttempts"'::regclass,'"ProjectVerifications"'::regclass) AND n.nspname<>source_schema)
    THEN RAISE EXCEPTION 'L1 source tables must belong to the same application schema'; END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE contype='f' AND
        ((confrelid='"ProjectModerations"'::regclass AND NOT
            ((conname='FK_EvidenceModerationAttempts_ProjectModerations_ModerationId_~' AND conrelid='"EvidenceModerationAttempts"'::regclass)
             OR (conname='FK_ProjectVerifications_ProjectModerations_EvidenceModerationI~' AND conrelid='"ProjectVerifications"'::regclass)))
         OR confrelid='"EvidenceModerationAttempts"'::regclass))
    THEN RAISE EXCEPTION 'Unexpected inbound L1 foreign key; inventory and migrate the dependency first'; END IF;
    IF EXISTS (SELECT 1 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled<>'D' AND
        tgrelid IN ('"ProjectModerations"'::regclass,'"EvidenceModerationAttempts"'::regclass,'"ProjectVerifications"'::regclass))
    THEN RAISE EXCEPTION 'Unexpected L1 source trigger; inspect side effects before migration'; END IF;
    archive_schema := CASE WHEN source_schema='public' THEN 'l1_archive' ELSE 'l1_archive_'||md5(source_schema) END;
    IF to_regnamespace(archive_schema) IS NOT NULL THEN
        RAISE EXCEPTION 'L1 archive schema already exists; refusing to overwrite %',archive_schema;
    END IF;
    EXECUTE format('CREATE SCHEMA %I',archive_schema);
    EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC',archive_schema);
    EXECUTE format('CREATE TABLE %I."Manifest" ("TableName" text PRIMARY KEY,"SourceSchema" text NOT NULL,"Fingerprint" jsonb NOT NULL,"ArchivedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP)',archive_schema);
    FOREACH table_name IN ARRAY ARRAY['ProjectModerations','EvidenceModerationAttempts'] LOOP
        EXECUTE format(fingerprint_sql,format('%I.%I',source_schema,table_name),source_schema,table_name) INTO expected;
        -- Preserve all old columns, including any retired fields on submission rows.
        EXECUTE format('CREATE TABLE %I.%I (LIKE %I.%I INCLUDING ALL)',archive_schema,table_name,source_schema,table_name);
        EXECUTE format('INSERT INTO %I.%I SELECT * FROM %I.%I',archive_schema,table_name,source_schema,table_name);
        EXECUTE format(fingerprint_sql,format('%I.%I',archive_schema,table_name),archive_schema,table_name) INTO actual;
        IF expected IS DISTINCT FROM actual THEN RAISE EXCEPTION 'L1 archive row/schema/checksum mismatch for %',table_name; END IF;
    END LOOP;
    EXECUTE format('CREATE TABLE %I."ProjectVerificationLevel1Links" AS SELECT "Id","ProjectId","EvidenceModerationId","EvidenceModerationPurpose" FROM %I."ProjectVerifications" WHERE "EvidenceModerationId" IS NOT NULL',archive_schema,source_schema);
    EXECUTE format('ALTER TABLE %I."ProjectVerificationLevel1Links" ADD PRIMARY KEY ("Id")',archive_schema);
    EXECUTE format('SELECT EXISTS ((SELECT "Id","ProjectId","EvidenceModerationId","EvidenceModerationPurpose" FROM %I."ProjectVerifications" WHERE "EvidenceModerationId" IS NOT NULL EXCEPT SELECT * FROM %I."ProjectVerificationLevel1Links") UNION ALL (SELECT * FROM %I."ProjectVerificationLevel1Links" EXCEPT SELECT "Id","ProjectId","EvidenceModerationId","EvidenceModerationPurpose" FROM %I."ProjectVerifications" WHERE "EvidenceModerationId" IS NOT NULL))',source_schema,archive_schema,archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'L1 verification link archive mismatch'; END IF;
    EXECUTE format($function$
        CREATE FUNCTION %I."RejectWrites"() RETURNS trigger LANGUAGE plpgsql AS $body$
        BEGIN RAISE EXCEPTION 'L1 archive is read-only; use the verified migration rollback'; END;
        $body$;
    $function$,archive_schema);
    FOREACH table_name IN ARRAY ARRAY['ProjectModerations','EvidenceModerationAttempts','ProjectVerificationLevel1Links'] LOOP
        EXECUTE format(fingerprint_sql,format('%I.%I',archive_schema,table_name),archive_schema,table_name) INTO actual;
        EXECUTE format('INSERT INTO %I."Manifest" ("TableName","SourceSchema","Fingerprint") VALUES ($1,$2,$3)',archive_schema)
            USING table_name,source_schema,actual;
        EXECUTE format('CREATE TRIGGER "L1_ReadOnly" BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I.%I FOR EACH STATEMENT EXECUTE FUNCTION %I."RejectWrites"()',archive_schema,table_name,archive_schema);
    END LOOP;
    EXECUTE format('CREATE TRIGGER "L1_ReadOnly" BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I."Manifest" FOR EACH STATEMENT EXECUTE FUNCTION %I."RejectWrites"()',archive_schema,archive_schema);
    -- The verified copies exist before any business relationship is detached.
    DELETE FROM "EvidenceModerationAttempts";
    UPDATE "ProjectVerifications" SET "EvidenceModerationId"=NULL WHERE "EvidenceModerationId" IS NOT NULL;
    DELETE FROM "ProjectModerations" WHERE "Purpose"=1;
END $archive$;
""";

    private const string ValidateLevel1ArchiveSql = """
SET LOCAL timezone = 'UTC';
SET LOCAL lock_timeout = '15s';
LOCK TABLE "ProjectModerations", "ProjectVerifications" IN ACCESS EXCLUSIVE MODE;
DO $validate$
DECLARE
    source_schema text;
    archive_schema text;
    table_name text;
    expected jsonb;
    actual jsonb;
    manifest_count bigint;
    fingerprint_sql text := 'SELECT jsonb_build_object(''count'',count(*),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex''),''columns'',(SELECT jsonb_agg(jsonb_build_array(attname,format_type(atttypid,atttypmod),attnotnull) ORDER BY attnum) FROM pg_attribute WHERE attrelid=%L::regclass AND attnum>0 AND NOT attisdropped)) FROM %I.%I t';
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectModerations"'::regclass;
    archive_schema := CASE WHEN source_schema='public' THEN 'l1_archive' ELSE 'l1_archive_'||md5(source_schema) END;
    IF to_regclass(format('%I."Manifest"',archive_schema)) IS NULL THEN
        RAISE EXCEPTION 'L1 archive manifest missing; rollback refused. Restore a verified full backup'; END IF;
    EXECUTE format('LOCK TABLE %I."Manifest" IN ACCESS EXCLUSIVE MODE',archive_schema);
    EXECUTE format('SELECT count(*) FROM %I."Manifest"',archive_schema) INTO manifest_count;
    IF manifest_count<>3 THEN RAISE EXCEPTION 'L1 archive manifest incomplete; rollback refused'; END IF;
    FOREACH table_name IN ARRAY ARRAY['ProjectModerations','EvidenceModerationAttempts','ProjectVerificationLevel1Links'] LOOP
        IF to_regclass(format('%I.%I',archive_schema,table_name)) IS NULL THEN RAISE EXCEPTION 'L1 archived table % missing; rollback refused',table_name; END IF;
        EXECUTE format('LOCK TABLE %I.%I IN ACCESS EXCLUSIVE MODE',archive_schema,table_name);
        EXECUTE format('SELECT "Fingerprint" FROM %I."Manifest" WHERE "TableName"=$1 AND "SourceSchema"=$2',archive_schema) INTO expected USING table_name,source_schema;
        EXECUTE format(fingerprint_sql,format('%I.%I',archive_schema,table_name),archive_schema,table_name) INTO actual;
        IF expected IS NULL OR expected IS DISTINCT FROM actual THEN RAISE EXCEPTION 'L1 archive checksum/schema mismatch for %; rollback refused',table_name; END IF;
    END LOOP;
END $validate$;
""";

    private const string RestoreLevel1Sql = """
DO $restore$
DECLARE
    source_schema text;
    archive_schema text;
    columns_sql text;
    assignments_sql text;
    table_name text;
    unsafe boolean;
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectModerations"'::regclass;
    archive_schema := CASE WHEN source_schema='public' THEN 'l1_archive' ELSE 'l1_archive_'||md5(source_schema) END;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I."ProjectModerations" a LEFT JOIN %I."Projects" p ON p."Id"=a."ProjectId" WHERE p."Id" IS NULL)',archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'L1 archive references a removed project; rollback refused'; END IF;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I."ProjectModerations" a LEFT JOIN %I."Users" u ON u."Id"=a."ReviewedByUserId" WHERE a."ReviewedByUserId" IS NOT NULL AND u."Id" IS NULL)',archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'L1 archive references a removed reviewer; rollback refused'; END IF;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I."ProjectModerations" a LEFT JOIN %I."ProjectModerations" m ON m."Id"=a."Id" WHERE (a."Purpose"=0 AND (m."Id" IS NULL OR m."ProjectId"<>a."ProjectId" OR m."Purpose"<>0)) OR (a."Purpose"=1 AND m."Id" IS NOT NULL))',archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'L1 restore ID collision or removed submission; rollback refused'; END IF;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I."ProjectVerificationLevel1Links" a LEFT JOIN %I."ProjectVerifications" v ON v."Id"=a."Id" WHERE v."Id" IS NULL OR v."ProjectId"<>a."ProjectId")',archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'L1 linked verification missing or changed project; rollback refused'; END IF;
    SELECT string_agg(format('%I',attname),',' ORDER BY attnum) INTO columns_sql FROM pg_attribute
        WHERE attrelid=to_regclass(format('%I."ProjectModerations"',archive_schema)) AND attnum>0 AND NOT attisdropped;
    EXECUTE format('INSERT INTO %I."ProjectModerations" (%s) SELECT %s FROM %I."ProjectModerations" WHERE "Purpose"=1',source_schema,columns_sql,columns_sql,archive_schema);
    SELECT string_agg(format('%I=a.%I',column_name,column_name),',') INTO assignments_sql FROM unnest(ARRAY[
        'AttemptCount','CompletedAt','LastError','LastErrorCode','LeaseExpiresAt','LeaseToken','NextRetryAt',
        'PolicyHash','PolicyJson','SnapshotJson','SourceHash','Summary','SupersededAt','Version']) column_name;
    -- Restore only retired columns on submissions; do not overwrite newer moderation decisions.
    EXECUTE format('UPDATE %I."ProjectModerations" m SET %s FROM %I."ProjectModerations" a WHERE m."Id"=a."Id" AND a."Purpose"=0',source_schema,assignments_sql,archive_schema);
    SELECT string_agg(format('%I',attname),',' ORDER BY attnum) INTO columns_sql FROM pg_attribute
        WHERE attrelid=to_regclass(format('%I."EvidenceModerationAttempts"',archive_schema)) AND attnum>0 AND NOT attisdropped;
    EXECUTE format('INSERT INTO %I."EvidenceModerationAttempts" (%s) SELECT %s FROM %I."EvidenceModerationAttempts"',source_schema,columns_sql,columns_sql,archive_schema);
    EXECUTE format('UPDATE %I."ProjectVerifications" v SET "EvidenceModerationId"=a."EvidenceModerationId","EvidenceModerationPurpose"=a."EvidenceModerationPurpose" FROM %I."ProjectVerificationLevel1Links" a WHERE v."Id"=a."Id"',source_schema,archive_schema);
    FOREACH table_name IN ARRAY ARRAY['ProjectVerificationLevel1Links','EvidenceModerationAttempts','ProjectModerations','Manifest'] LOOP
        EXECUTE format('DROP TABLE %I.%I',archive_schema,table_name);
    END LOOP;
    EXECUTE format('DROP FUNCTION %I."RejectWrites"()',archive_schema);
    -- No CASCADE: unexpected archive objects must be reviewed, never silently removed.
    EXECUTE format('DROP SCHEMA %I',archive_schema);
END $restore$;
""";
}
