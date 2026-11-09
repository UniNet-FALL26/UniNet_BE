# Archive and remove legacy verification evidence

Status: prepared migration only. Do not apply to an application database without separately approving the target and backup.

Migration: `20261109013212_ArchiveAndRemoveVerificationEvidence`, after `20261108142140_ArchiveAndRemoveLevel1`.

## Scope

- Remove the `ProjectVerificationEvidence` entity, DbSet, mapping and `ProjectVerification.EvidenceSubmissions` navigation.
- Archive all historical evidence rows, including every version, submitter, timestamp and JSON payload, before dropping the active table.
- Preserve project verification cases, fees, assignments, meetings, assessment reports and shared notifications.
- Keep historical migrations unchanged. Their references describe earlier database states and are required for upgrades and rollback.

## Archive and rollback

For the public application schema the archive is `verification_evidence_archive`; other application schemas use `verification_evidence_archive_<md5(schema)>`.

The archive contains `ProjectVerificationEvidence`, `VerificationParents`, `SubmitterParents` and `Manifest`. Row counts, SHA-256 fingerprints and column metadata are verified before the active table is dropped. Statement triggers reject archive writes. The archive has no live foreign keys, so removing an application case cannot cascade-delete its archived evidence. Database owners can bypass triggers; therefore this is an operational safeguard rather than a security boundary against privileged administrators.

Rollback verifies every archived table and the original parent identities before recreating the active table, constraints and indexes. Missing/tampered archives, missing/reassociated parents and unexpected archive objects cause rollback to fail transactionally. Rollback does not resurrect deleted cases or overwrite current relationships.

## Deployment steps (not executed)

1. Review applied migration history, actual table schema, row count, incoming dependencies, triggers and archive-name collisions on the approved target. Stop for unknown dependencies.
2. Pause writes for deployment and take a full custom-format PostgreSQL backup including all application and archive schemas. Verify the backup by restoring it into an isolated database.
3. Review generated forward SQL for this migration and the preceding L1 migration if that migration is still pending. Apply through the normal transactional EF migration runner; do not disable transactions.
4. Verify migration history, archive manifest, absence of the active evidence table and continued operation of payment, booking, assessment and notifications.
5. Keep the archive and external backup under the team's retention policy. Do not purge either as part of this change.

When rolling back, use the EF target `20261108142140_ArchiveAndRemoveLevel1` to undo only this migration. Rolling back further also invokes the independent L1 archive checks. Never replace a failed verified rollback with an empty recreated table.

## Validation completed

- Build: zero errors and warnings. EF reports no pending model changes.
- Isolated PostgreSQL: 17/17 passed (seven evidence archive tests and ten preceding L1 archive tests). Evidence: `artifacts/verification-g1/20261109T014247197Z/g1-postgres.trx`.
- Evidence tests cover populated/empty round trips, preserved workflow data, archive write protection, checksum tampering, missing archives, missing parents, unexpected inbound foreign keys and actual custom-format backup/restore.
- Related verification/payment/notification regression tests: 181/181 passed. Evidence: `artifacts/remove-l1-tests/verification-evidence-regression.trx`.
- Forward and rollback SQL generated for review at `artifacts/remove-l1-tests/verification-evidence-up.sql` and `verification-evidence-down.sql`; neither was executed on an application database.
