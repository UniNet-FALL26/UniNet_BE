# Archive and remove Level 1 storage

This migration is prepared for review and testing. It must not be applied to the application's live database without an explicit deployment decision and a verified full backup. The implementation and test process use a separate temporary PostgreSQL cluster; they do not read the application's `.env` connection.

## Migration and retained data

- Previous migration: `20261108023501_AddAssessorPasswordTokenIndex`.
- Target migration: `20261108142140_ArchiveAndRemoveLevel1`.
- For `public`, the archive schema is `l1_archive`. For any other application schema it is `l1_archive_` followed by the MD5 of the schema name, so independently hosted schemas cannot overwrite one another's archives.
- The archive contains full pre-migration `ProjectModerations` rows, full `EvidenceModerationAttempts` rows, and the non-null verification-to-L1 links with their original project IDs. Submission rows are also captured to preserve any retired metadata attached to them.
- A read-only manifest records source schema, row counts, column metadata and SHA-256 fingerprints. The archive and manifest reject INSERT, UPDATE, DELETE and TRUNCATE statements.
- After verified copying, active L1 rows and attempts are removed. The live model drops the L1 relationship and columns, attempts table, obsolete indexes and constraints. `ProjectModerations` continues to store project-creation moderation and only permits Purpose 0.
- Projects, project documents, tasks, payment rows, amounts, booking details, assignments, final reports, refund records and account verification are retained. Old notification records are retained but do not open retired L1 screens.

## Before a live deployment

1. Identify and independently confirm the intended database and application schema. Record the currently applied migration. Do not rely on a default connection or an automatically discovered `.env` value.
2. Run [the read-only inventory](../migrations/archive-remove-level1-preflight.sql) with `psql -X -w --set=ON_ERROR_STOP=1 --file=...` using an explicitly selected connection. Investigate unknown inbound foreign keys or unexpected existing archive schemas before proceeding.
3. Take a full custom-format backup with `pg_dump -Fc`, including every application and archive schema. Store the dump outside the repository and record its SHA-256. Use the password environment or a protected PostgreSQL password file, not a password embedded in a logged command.
4. Restore that backup into a separate database and verify it. Apply this migration and run the inventory and functional smoke tests there. A backup that has not been restored is not sufficient deployment evidence.
5. Review archive counts and fingerprints against the clone baseline. Check payment totals, project moderation decisions, assignments and report contents.
6. Arrange a maintenance window. Stop all application instances and workers that still use the old model. Old versions must not continue writing L1 data or expecting the removed columns.
7. With final approval and the intended connection explicitly configured, apply the target migration, then deploy/start the matching backend. The migration is transactional, takes exclusive table locks, and fails if it cannot acquire a lock within 15 seconds. Do not bypass a failure by dropping constraints or deleting the existing archive.

## After migration

Check that archive tables and manifest exist, active `ProjectModerations` has only Purpose 0, L1 columns/attempts are absent from the active schema, and submission moderation still works. Verify case creation without L1, paid assignment, meeting access and report retrieval. Compare payment totals and existing reports to the inventory. Retain the external backup and the archive; do not drop the archive to save space.

Application runtime credentials should not own or have DDL rights over the archive. The migration owner necessarily can bypass triggers; access control and external backups are still required. If the same credential currently owns the entire application schema, fix deployment privileges separately rather than assuming triggers prevent owner-level tampering.

## Rollback

Stop the matching backend before downgrading to the previous migration. Rollback first locks and verifies all archive rows, columns and fingerprints. It then restores the old columns, L1 records, provider attempts and original case links. Retired fields on surviving submission rows are restored without overwriting newer submission decisions.

Rollback refuses missing/corrupted archives, ID collisions, deleted submission rows, missing projects/reviewers, and removed or reassociated linked verification requests. It does not silently recreate empty history or overwrite a new record. Unknown archive objects prevent archive cleanup because no CASCADE is used. If rollback is refused, investigate or restore the verified full backup; do not manufacture a new manifest.

New verification requests created after migration return to the old schema with a null L1 link. The already-retired L1 workflow must not be re-enabled merely because a database downgrade was required.

## Prepared validation evidence

Ten migration tests passed on an isolated PostgreSQL 18 cluster, including a real custom-format backup, schema restore and verified downgrade. An additional nine booking/concurrency tests and one earlier migration guard passed in the wider PostgreSQL run. Seven older fixture failures were reproduced on unchanged HEAD, all due to missing `SessionVersion` in the older seeded schema before the new migration runs. The full non-PostgreSQL suite retains four previously confirmed unrelated failures. See [the handoff](../handoffs/remove-verification-l1-prerequisite.md) for counts and result paths.

The generated forward/reverse SQL files are under `artifacts/remove-l1-tests/archive-remove-l1-up.sql` and `archive-remove-l1-down.sql`. The current model snapshot matches the migration. No application database backup or live migration has been performed; those require confirmation of the intended deployment target.
