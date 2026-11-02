# Store role-specific user details as JSONB

Status: Accepted by user request, 2026-11-02.

Users contains common identity/contact/system columns alongside role-specific student and partner columns. Adding nine assessor fields would extend a sparse shared table. The user requested one JSON document per role while preserving existing CRUD.

Use EF Core 8 owned ToJson documents StudentJson, PartnerJson and AssessorJson. Keep relational account identity, role, status, verification, contacts and cross-table foreign keys unchanged. Preserve legacy flat API fields through adapters and query JSON members explicitly in SQL-backed LINQ. Typed DTO validation prevents unstructured role payloads.

Separate profile tables would provide stronger relational constraints but do not match the requested grouping. String-only JSON converters would complicate SQL-backed searches and change tracking. Upgrading EF is unnecessary because the installed Npgsql 8 provider supports owned JSON mappings.

Consequences: migration/backfill and matching backend rollout are required; application validation owns JSON field length limits. Existing student lookup uses an explicit expression index. Assessor fields have no old-schema equivalent, so rollback guards against silently dropping those documents. See ../handoffs/user-role-json.md for commands and verification.

Sources: https://www.npgsql.org/efcore/mapping/json.html and https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew
