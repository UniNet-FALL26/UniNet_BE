# Legacy Project Task / Evidence / Report API retirement

Task storage and business workflows are retired. Use [Official Contribution V2](UNINET_OFFICIAL_CONTRIBUTION_V2_API.md) for Git/Peer contribution. This is a source contract; database rollout requires its own approval.

## Compatibility boundary

The following existing routes remain under `/api/projects/{projectId}`. They require the existing Student JWT and valid subject claim. A valid request returns:

```json
{"code":"PROJECT_FEATURE_RETIRED","message":"Project workspace, tasks and external drafts have been retired."}
```

HTTP status is **410 Gone**. No Task service, database query/write, upload, assignment, report versioning or review action occurs. Request field names/types remain for client transition; successful Task/report response DTOs are no longer implemented. Malformed/missing JSON retains normal ApiController400 behavior; missing/invalid authorization remains401/403.

| Method | Route suffix |
|---|---|
| GET, POST | `/tasks` |
| GET, PUT, DELETE | `/tasks/{taskId}` |
| PATCH | `/tasks/{taskId}/status` |
| PATCH | `/tasks/{taskId}/assignments` |
| GET, POST | `/tasks/{taskId}/reports` |
| POST | `/tasks/{taskId}/reports/drafts` |
| GET, PUT, DELETE | `/tasks/{taskId}/reports/{reportId}` |
| PUT | `/tasks/{taskId}/reports/{reportId}/draft` |
| POST | `/tasks/{taskId}/reports/{reportId}/submit` |
| POST | `/tasks/{taskId}/reports/{reportId}/withdraw` |
| POST | `/tasks/{taskId}/reports/{reportId}/reviews` |

Task Evidence used to be attached through report request bodies. Those retained request fields do not upload or persist files. Generic uploads, Project documents and Verification evidence/reports are independent and are not retired by this boundary.

## Legacy Contribution V1

- Authorized historical Cycle, participant, Run, review and finalized-result reads remain.
- `POST /contribution/calculations` cannot create a Task-based Run; after existing account/Creator/cycle checks it returns410 `CONTRIBUTION_INPUTS_RETIRED`.
- Legacy confirmation requiring retired live Task inputs also returns410. Existing historical revision-feedback handling is retained; it cannot create a replacement calculation or finalize results without those inputs.
- Official V2 projects retain their workflow guard and must use the V2 lifecycle. V1 history is not silently adopted or rewritten.
- Shared Peer conversion and legacy pure-calculation contracts remain for historical compatibility; Official Git calculation does not obtain Task inputs from them.

## Storage and rollout

The existing `20261116030240_ArchiveAndRemoveLegacyProjectTables` migration archives the five Task-related tables and six other intentionally retired/refactored tables. No second DropTable migration was added. Archived source rows and immutable V1/Git/Peer records are retained.

Do not assume a live database has applied this migration. This cleanup does not authorize Neon/E2E DDL, file deletion, schema-history stamping or rollback. Full dependency/verification evidence: [cleanup report](../agent-handoff/UNINET_REMOVE_LEGACY_TASK_REPORT_CLEANUP.md).
