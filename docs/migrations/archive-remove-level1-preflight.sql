-- Read-only inventory. Run against the explicitly selected application database
-- before scheduling the migration. No source text, documents or credentials are printed.
BEGIN READ ONLY;
SELECT current_database() AS database_name, n.nspname AS application_schema
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectModerations"'::regclass;
SELECT "Purpose", "Status", count(*) AS rows
FROM "ProjectModerations" GROUP BY "Purpose", "Status" ORDER BY "Purpose", "Status";
SELECT count(*) AS evidence_attempts FROM "EvidenceModerationAttempts";
SELECT count(*) FILTER (WHERE "EvidenceModerationId" IS NOT NULL) AS linked_cases,
       count(*) AS total_cases FROM "ProjectVerifications";
SELECT count(*) AS payments, coalesce(sum("Amount"),0) AS payment_amount FROM "Transactions";
SELECT count(*) AS assignments FROM "VerificationAssignments";
SELECT count(*) AS reports FROM "VerificationReports";
SELECT count(*) AS orphan_attempts
FROM "EvidenceModerationAttempts" a LEFT JOIN "ProjectModerations" m
  ON m."Id"=a."ModerationId" AND m."Purpose"=a."ModerationPurpose"
WHERE m."Id" IS NULL;
SELECT count(*) AS orphan_links
FROM "ProjectVerifications" v LEFT JOIN "ProjectModerations" m
  ON m."Id"=v."EvidenceModerationId" AND m."Purpose"=v."EvidenceModerationPurpose"
WHERE v."EvidenceModerationId" IS NOT NULL AND m."Id" IS NULL;
SELECT conname AS inbound_fk, conrelid::regclass::text AS dependent_table,
       pg_get_constraintdef(oid) AS definition
FROM pg_constraint WHERE contype='f' AND confrelid IN
  ('"ProjectModerations"'::regclass, '"EvidenceModerationAttempts"'::regclass)
ORDER BY conname;
SELECT tgname AS custom_trigger, tgrelid::regclass::text AS source_table,
       pg_get_triggerdef(oid) AS definition
FROM pg_trigger WHERE NOT tgisinternal AND tgenabled<>'D' AND tgrelid IN
  ('"ProjectModerations"'::regclass,'"EvidenceModerationAttempts"'::regclass,'"ProjectVerifications"'::regclass);
SELECT nspname AS existing_l1_archive FROM pg_namespace
WHERE nspname=(SELECT CASE WHEN n.nspname='public' THEN 'l1_archive'
                          ELSE 'l1_archive_'||md5(n.nspname) END
               FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='"ProjectModerations"'::regclass);
COMMIT;
