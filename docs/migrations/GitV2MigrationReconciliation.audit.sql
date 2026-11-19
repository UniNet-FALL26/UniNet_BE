-- Read-only schema/history inspection. Does not apply or stamp migrations.
-- Run only after verifying Docker identity and the effective connection target.
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '30s';
DO $$ BEGIN
  IF current_database() NOT IN ('uninet_github_app_e2e', 'uninet_v2_restore_disposable', 'uninet_v2_three_disposable')
     OR current_user <> 'uninet_test' THEN
    RAISE EXCEPTION 'Unexpected reconciliation audit database/user';
  END IF;
END $$;

SELECT current_database() AS database, current_user AS username,
       current_setting('transaction_read_only') AS read_only;
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";

SELECT n.nspname AS schema, c.relname AS table_name, a.attname AS column_name,
       format_type(a.atttypid,a.atttypmod) AS data_type, a.attnotnull AS not_null,
       pg_get_expr(d.adbin,d.adrelid) AS default_expression
FROM pg_attribute a
JOIN pg_class c ON c.oid=a.attrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
WHERE a.attnum>0 AND NOT a.attisdropped AND c.relkind IN ('r','p')
  AND n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'
ORDER BY schema,table_name,a.attnum;

SELECT n.nspname AS schema, c.relname AS table_name, k.conname,
       k.contype, k.convalidated, pg_get_constraintdef(k.oid,true) AS definition
FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'
ORDER BY schema,table_name,k.conname;

SELECT n.nspname AS schema, c.relname AS table_name, i.relname AS index_name,
       x.indisunique, x.indisvalid, x.indisready,
       pg_get_indexdef(x.indexrelid) AS definition
FROM pg_index x JOIN pg_class c ON c.oid=x.indrelid
JOIN pg_class i ON i.oid=x.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'
ORDER BY schema,table_name,index_name;

SELECT n.nspname AS schema, c.relname AS table_name, t.tgname,
       t.tgenabled, pg_get_triggerdef(t.oid,true) AS definition
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE NOT t.tgisinternal AND n.nspname NOT LIKE 'pg_%'
ORDER BY schema,table_name,t.tgname;

-- Counts only: do not disclose archived business evidence or account secrets.
SELECT 'Transactions' AS source,count(*) AS rows FROM "Transactions"
UNION ALL SELECT 'VerificationTransactions',count(*) FROM "VerificationTransactions"
UNION ALL SELECT 'ProjectModerations',count(*) FROM "ProjectModerations"
UNION ALL SELECT 'EvidenceModerationAttempts',count(*) FROM "EvidenceModerationAttempts"
UNION ALL SELECT 'ProjectVerificationEvidence',count(*) FROM "ProjectVerificationEvidence"
UNION ALL SELECT 'ProjectVerifications',count(*) FROM "ProjectVerifications"
UNION ALL SELECT 'ExternalProjectDrafts',count(*) FROM "ExternalProjectDrafts"
UNION ALL SELECT 'ExternalProjectDraftFiles',count(*) FROM "ExternalProjectDraftFiles"
UNION ALL SELECT 'ProjectEvidenceDocuments',count(*) FROM "ProjectEvidenceDocuments";
COMMIT;
