\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '120s';
SET LOCAL timezone = 'UTC';
SELECT jsonb_build_object('section','database','data',jsonb_build_object('database',current_database(),'server_version',current_setting('server_version'),'captured_at',transaction_timestamp(),'read_only',current_setting('transaction_read_only')));
SELECT jsonb_build_object('section','tables','data',jsonb_agg(jsonb_build_object('schema',schemaname,'table',tablename) ORDER BY schemaname,tablename)) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');
-- Use actual catalogs, including inbound FKs outside the planned cleanup tables.
SELECT jsonb_build_object('section','constraints','data',jsonb_agg(jsonb_build_object('schema',n.nspname,'table',r.relname,'name',c.conname,'type',c.contype,'validated',c.convalidated,'definition',pg_get_constraintdef(c.oid),'target',c.confrelid::regclass::text) ORDER BY n.nspname,r.relname,c.conname)) FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema');
SELECT jsonb_build_object('section','columns','data',jsonb_agg(to_jsonb(c) ORDER BY table_schema,table_name,ordinal_position)) FROM information_schema.columns c WHERE table_schema NOT IN ('pg_catalog','information_schema');
SELECT jsonb_build_object('section','indexes','data',jsonb_agg(to_jsonb(i) ORDER BY schemaname,tablename,indexname)) FROM pg_indexes i WHERE schemaname NOT IN ('pg_catalog','information_schema');
SELECT format('SELECT jsonb_build_object(''section'',%L,''data'',coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),''[]''::jsonb)) FROM %I.%I t;', 'migrations',schemaname,tablename) FROM pg_tables WHERE tablename='__EFMigrationsHistory'
\gexec
-- Each ID and a fingerprint of the COMPLETE row, sorted deterministically; no evidence/report content in this output.
SELECT format('SELECT jsonb_build_object(''section'',%L,''count'',count(*),''keys'',coalesce(jsonb_agg(jsonb_build_object(''id'',to_jsonb(t)->''Id'',''row_md5'',md5(to_jsonb(t)::text)) ORDER BY to_jsonb(t)->>''Id''),''[]''::jsonb)) FROM %I.%I t;', 'rows:'||schemaname||'.'||tablename,schemaname,tablename) FROM pg_tables WHERE tablename IN ('VerificationAdminAction','VerificationReportVersions','ProjectModerations','ProjectEvidenceModerations','EvidenceModerationAttempts','VerificationNotification','ProjectVerifications','VerificationReports','VerificationAssignments','RefundTransactions','VerificationTransactions','ReportAuditLogs') ORDER BY schemaname,tablename
\gexec
-- JSON access supports schemas before D/E or before the level-1 link migration without assuming missing columns exist.
SELECT format('SELECT jsonb_build_object(''section'',%L,''data'',coalesce(jsonb_agg(jsonb_build_object(''id'',to_jsonb(t)->''Id'',''evidence_id'',to_jsonb(t)->''EvidenceModerationId'',''status'',to_jsonb(t)->''Status'',''finalized_at'',to_jsonb(t)->''FinalizedAt'',''finalizer'',to_jsonb(t)->''FinalizedByUserId'') ORDER BY to_jsonb(t)->>''Id''),''[]''::jsonb)) FROM %I.%I t;', 'cases',schemaname,tablename) FROM pg_tables WHERE tablename='ProjectVerifications'
\gexec
SELECT format('SELECT jsonb_build_object(''section'',%L,''data'',coalesce(jsonb_agg(jsonb_build_object(''id'',to_jsonb(t)->''Id'',''source'',coalesce(to_jsonb(t)->''RequestSource'',''null''::jsonb),''actor'',to_jsonb(t)->''RequestedByUserId'',''requested_at'',to_jsonb(t)->''RequestedAt'') ORDER BY to_jsonb(t)->>''Id''),''[]''::jsonb)) FROM %I.%I t;', 'refunds',schemaname,tablename) FROM pg_tables WHERE tablename='RefundTransactions'
\gexec
SELECT format('SELECT jsonb_build_object(''section'',%L,''data'',coalesce(jsonb_agg(to_jsonb(g) ORDER BY g.report_id),''[]''::jsonb)) FROM (SELECT "ReportId"::text report_id,count(*) revisions,max("Version") last_version FROM %I.%I GROUP BY "ReportId") g;', 'revision_counts',schemaname,tablename) FROM pg_tables WHERE tablename='VerificationReportVersions'
\gexec
SELECT format('SELECT jsonb_build_object(''section'',%L,''data'',coalesce(jsonb_agg(jsonb_build_object(''id'',to_jsonb(t)->''Id'',''assignment_id'',coalesce(to_jsonb(t)->''AssignmentId'',to_jsonb(t)->''VerificationId''),''case_id'',to_jsonb(t)->''ProjectVerificationId'',''outcome'',to_jsonb(t)->''Outcome'',''has_original'',to_jsonb(t)->>''LegacyOriginalJson'' IS NOT NULL) ORDER BY to_jsonb(t)->>''Id''),''[]''::jsonb)) FROM %I.%I t;', 'reports',schemaname,tablename) FROM pg_tables WHERE tablename='VerificationReports'
\gexec
SELECT format('SELECT jsonb_build_object(''section'',%L,''data'',coalesce(jsonb_agg(jsonb_build_object(''id'',"Id",''evidence_id'',to_jsonb(t)->''EvidenceModerationId'') ORDER BY "Id"),''[]''::jsonb)) FROM %I.%I t WHERE to_jsonb(t)->>''EvidenceModerationId'' IS NOT NULL;', 'evidence_notifications',schemaname,tablename) FROM pg_tables WHERE tablename='VerificationNotification'
\gexec
-- Check every real FK (including composite keys), not just EF navigations. MATCH SIMPLE excludes null components.
SELECT format('SELECT jsonb_build_object(''section'',%L,''orphans'',count(*)) FROM %s s WHERE %s AND NOT EXISTS (SELECT 1 FROM %s d WHERE %s);', 'fk:'||c.conrelid::regclass::text||':'||c.conname,c.conrelid::regclass,string_agg(format('s.%I IS NOT NULL',a.attname),' AND ' ORDER BY k.ord),c.confrelid::regclass,string_agg(format('s.%I = d.%I',a.attname,b.attname),' AND ' ORDER BY k.ord)) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace CROSS JOIN LATERAL unnest(c.conkey,c.confkey) WITH ORDINALITY k(src,dst,ord) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.src JOIN pg_attribute b ON b.attrelid=c.confrelid AND b.attnum=k.dst WHERE c.contype='f' AND n.nspname NOT IN ('pg_catalog','information_schema') GROUP BY c.oid,c.conrelid,c.confrelid,c.conname ORDER BY c.conrelid::regclass::text,c.conname
\gexec
SELECT format('SELECT jsonb_build_object(''section'',''moderation_id_collisions'',''data'',coalesce(jsonb_agg(a."Id" ORDER BY a."Id"),''[]''::jsonb)) FROM %I."ProjectModerations" a JOIN %I."ProjectEvidenceModerations" b ON a."Id"=b."Id";',a.schemaname,b.schemaname) FROM pg_tables a JOIN pg_tables b ON a.schemaname=b.schemaname WHERE a.tablename='ProjectModerations' AND b.tablename='ProjectEvidenceModerations'
\gexec
COMMIT;
