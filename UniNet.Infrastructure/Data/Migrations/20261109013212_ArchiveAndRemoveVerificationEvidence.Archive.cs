namespace UniNet.Infrastructure.Data.Migrations;

public partial class ArchiveAndRemoveVerificationEvidence
{
    private const string ArchiveEvidenceSql = """
SET LOCAL timezone = 'UTC';
SET LOCAL lock_timeout = '15s';
LOCK TABLE "ProjectVerificationEvidence", "ProjectVerifications", "Users" IN ACCESS EXCLUSIVE MODE;
DO $archive$
DECLARE
    source_schema text;
    archive_schema text;
    table_name text;
    expected jsonb;
    actual jsonb;
    fingerprint_sql text := 'SELECT jsonb_build_object(''count'',count(*),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex''),''columns'',(SELECT jsonb_agg(jsonb_build_array(attname,format_type(atttypid,atttypmod),attnotnull) ORDER BY attnum) FROM pg_attribute WHERE attrelid=%L::regclass AND attnum>0 AND NOT attisdropped)) FROM %I.%I t';
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectVerificationEvidence"'::regclass;
    IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE c.oid IN ('"ProjectVerifications"'::regclass,'"Users"'::regclass) AND n.nspname<>source_schema)
    THEN RAISE EXCEPTION 'Verification evidence source and parent tables must share the application schema'; END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE contype='f' AND confrelid='"ProjectVerificationEvidence"'::regclass)
    THEN RAISE EXCEPTION 'Unexpected inbound verification evidence FK; inventory the dependency before removal'; END IF;
    IF EXISTS (SELECT 1 FROM pg_trigger WHERE NOT tgisinternal AND tgenabled<>'D' AND tgrelid='"ProjectVerificationEvidence"'::regclass)
    THEN RAISE EXCEPTION 'Unexpected verification evidence trigger; review before removal'; END IF;
    archive_schema := CASE WHEN source_schema='public' THEN 'verification_evidence_archive' ELSE 'verification_evidence_archive_'||md5(source_schema) END;
    IF to_regnamespace(archive_schema) IS NOT NULL THEN RAISE EXCEPTION 'Verification evidence archive already exists; refusing to overwrite %',archive_schema; END IF;
    EXECUTE format('CREATE SCHEMA %I',archive_schema);
    EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC',archive_schema);
    EXECUTE format('CREATE TABLE %I."Manifest" ("TableName" text PRIMARY KEY,"SourceSchema" text NOT NULL,"Fingerprint" jsonb NOT NULL,"ArchivedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP)',archive_schema);
    EXECUTE format(fingerprint_sql,format('%I."ProjectVerificationEvidence"',source_schema),source_schema,'ProjectVerificationEvidence') INTO expected;
    EXECUTE format('CREATE TABLE %I."ProjectVerificationEvidence" (LIKE %I."ProjectVerificationEvidence" INCLUDING ALL)',archive_schema,source_schema);
    EXECUTE format('INSERT INTO %I."ProjectVerificationEvidence" SELECT * FROM %I."ProjectVerificationEvidence"',archive_schema,source_schema);
    EXECUTE format(fingerprint_sql,format('%I."ProjectVerificationEvidence"',archive_schema),archive_schema,'ProjectVerificationEvidence') INTO actual;
    IF expected IS DISTINCT FROM actual THEN RAISE EXCEPTION 'Verification evidence archive row/schema/checksum mismatch'; END IF;
    -- Parent identities are frozen separately; archive data must not cascade-delete with live cases.
    EXECUTE format('CREATE TABLE %I."VerificationParents" AS SELECT v."Id",v."ProjectId",v."RequestByUserId" FROM %I."ProjectVerifications" v WHERE EXISTS (SELECT 1 FROM %I."ProjectVerificationEvidence" e WHERE e."ProjectVerificationId"=v."Id")',archive_schema,source_schema,source_schema);
    EXECUTE format('ALTER TABLE %I."VerificationParents" ADD PRIMARY KEY ("Id")',archive_schema);
    EXECUTE format('CREATE TABLE %I."SubmitterParents" AS SELECT u."Id",u."AccountId" FROM %I."Users" u WHERE EXISTS (SELECT 1 FROM %I."ProjectVerificationEvidence" e WHERE e."SubmittedByUserId"=u."Id")',archive_schema,source_schema,source_schema);
    EXECUTE format('ALTER TABLE %I."SubmitterParents" ADD PRIMARY KEY ("Id")',archive_schema);
    EXECUTE format($function$
        CREATE FUNCTION %I."RejectWrites"() RETURNS trigger LANGUAGE plpgsql AS $body$
        BEGIN RAISE EXCEPTION 'Verification evidence archive is read-only; use the verified migration rollback'; END;
        $body$;
    $function$,archive_schema);
    FOREACH table_name IN ARRAY ARRAY['ProjectVerificationEvidence','VerificationParents','SubmitterParents'] LOOP
        EXECUTE format(fingerprint_sql,format('%I.%I',archive_schema,table_name),archive_schema,table_name) INTO actual;
        EXECUTE format('INSERT INTO %I."Manifest" ("TableName","SourceSchema","Fingerprint") VALUES ($1,$2,$3)',archive_schema) USING table_name,source_schema,actual;
        EXECUTE format('CREATE TRIGGER "Evidence_ReadOnly" BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I.%I FOR EACH STATEMENT EXECUTE FUNCTION %I."RejectWrites"()',archive_schema,table_name,archive_schema);
    END LOOP;
    EXECUTE format('CREATE TRIGGER "Evidence_ReadOnly" BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %I."Manifest" FOR EACH STATEMENT EXECUTE FUNCTION %I."RejectWrites"()',archive_schema,archive_schema);
END $archive$;
""";

    private const string ValidateEvidenceArchiveSql = """
SET LOCAL timezone = 'UTC';
SET LOCAL lock_timeout = '15s';
LOCK TABLE "ProjectVerifications", "Users" IN ACCESS EXCLUSIVE MODE;
DO $validate$
DECLARE
    source_schema text;
    archive_schema text;
    table_name text;
    expected jsonb;
    actual jsonb;
    manifest_count bigint;
    unsafe boolean;
    fingerprint_sql text := 'SELECT jsonb_build_object(''count'',count(*),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex''),''columns'',(SELECT jsonb_agg(jsonb_build_array(attname,format_type(atttypid,atttypmod),attnotnull) ORDER BY attnum) FROM pg_attribute WHERE attrelid=%L::regclass AND attnum>0 AND NOT attisdropped)) FROM %I.%I t';
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectVerifications"'::regclass;
    archive_schema := CASE WHEN source_schema='public' THEN 'verification_evidence_archive' ELSE 'verification_evidence_archive_'||md5(source_schema) END;
    IF to_regclass(format('%I."Manifest"',archive_schema)) IS NULL THEN RAISE EXCEPTION 'Verification evidence archive manifest missing; rollback refused'; END IF;
    EXECUTE format('LOCK TABLE %I."Manifest" IN ACCESS EXCLUSIVE MODE',archive_schema);
    EXECUTE format('SELECT count(*) FROM %I."Manifest"',archive_schema) INTO manifest_count;
    IF manifest_count<>3 THEN RAISE EXCEPTION 'Verification evidence archive manifest incomplete; rollback refused'; END IF;
    FOREACH table_name IN ARRAY ARRAY['ProjectVerificationEvidence','VerificationParents','SubmitterParents'] LOOP
        IF to_regclass(format('%I.%I',archive_schema,table_name)) IS NULL THEN RAISE EXCEPTION 'Verification evidence archived table % missing; rollback refused',table_name; END IF;
        EXECUTE format('LOCK TABLE %I.%I IN ACCESS EXCLUSIVE MODE',archive_schema,table_name);
        EXECUTE format('SELECT "Fingerprint" FROM %I."Manifest" WHERE "TableName"=$1 AND "SourceSchema"=$2',archive_schema) INTO expected USING table_name,source_schema;
        EXECUTE format(fingerprint_sql,format('%I.%I',archive_schema,table_name),archive_schema,table_name) INTO actual;
        IF expected IS NULL OR expected IS DISTINCT FROM actual THEN RAISE EXCEPTION 'Verification evidence archive checksum/schema mismatch; rollback refused'; END IF;
    END LOOP;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I."VerificationParents" a LEFT JOIN %I."ProjectVerifications" v ON v."Id"=a."Id" WHERE v."Id" IS NULL OR v."ProjectId" IS DISTINCT FROM a."ProjectId" OR v."RequestByUserId" IS DISTINCT FROM a."RequestByUserId")',archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'Verification evidence parent case missing or reassociated; rollback refused'; END IF;
    EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I."SubmitterParents" a LEFT JOIN %I."Users" u ON u."Id"=a."Id" WHERE u."Id" IS NULL OR u."AccountId" IS DISTINCT FROM a."AccountId")',archive_schema,source_schema) INTO unsafe;
    IF unsafe THEN RAISE EXCEPTION 'Verification evidence submitter missing or reassociated; rollback refused'; END IF;
    IF to_regclass(format('%I."ProjectVerificationEvidence"',source_schema)) IS NOT NULL THEN RAISE EXCEPTION 'Active verification evidence table already exists; rollback refused'; END IF;
END $validate$;
""";

    private const string RestoreEvidenceSql = """
DO $restore$
DECLARE
    source_schema text;
    archive_schema text;
    columns_sql text;
    table_name text;
    expected jsonb;
    actual jsonb;
BEGIN
    SELECT n.nspname INTO source_schema FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectVerifications"'::regclass;
    archive_schema := CASE WHEN source_schema='public' THEN 'verification_evidence_archive' ELSE 'verification_evidence_archive_'||md5(source_schema) END;
    SELECT string_agg(format('%I',attname),',' ORDER BY attnum) INTO columns_sql FROM pg_attribute
        WHERE attrelid=to_regclass(format('%I."ProjectVerificationEvidence"',archive_schema)) AND attnum>0 AND NOT attisdropped;
    EXECUTE format('INSERT INTO %I."ProjectVerificationEvidence" (%s) SELECT %s FROM %I."ProjectVerificationEvidence"',source_schema,columns_sql,columns_sql,archive_schema);
    EXECUTE format('SELECT "Fingerprint" FROM %I."Manifest" WHERE "TableName"=''ProjectVerificationEvidence''',archive_schema) INTO expected;
    EXECUTE format('SELECT jsonb_build_object(''count'',count(*),''sha256'',encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(t)::text,''UTF8'')),''hex''),''|'' ORDER BY "Id"),''''),''UTF8'')),''hex'')) FROM %I."ProjectVerificationEvidence" t',source_schema) INTO actual;
    IF actual->'count' IS DISTINCT FROM expected->'count' OR actual->'sha256' IS DISTINCT FROM expected->'sha256'
    THEN RAISE EXCEPTION 'Restored verification evidence checksum mismatch'; END IF;
    FOREACH table_name IN ARRAY ARRAY['ProjectVerificationEvidence','VerificationParents','SubmitterParents','Manifest'] LOOP
        EXECUTE format('DROP TABLE %I.%I',archive_schema,table_name);
    END LOOP;
    EXECUTE format('DROP FUNCTION %I."RejectWrites"()',archive_schema);
    -- Unknown archive objects abort rollback instead of being silently deleted.
    EXECUTE format('DROP SCHEMA %I',archive_schema);
END $restore$;
""";
}
