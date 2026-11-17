# Public project details and direct friend invitations

Authenticated active students may read all project details. Reading never grants edit or workspace membership. Remove privacy controls; retain the legacy visibility column/contract for compatibility and normalize Hub writes to public.

Applications require open, unexpired recruitment and an available posted position. Direct invitations require the project owner, an active/pending project, an accepted student friend and a required role (150 characters maximum). They do not require recruitment or reserve a posted position. Only recipient acceptance creates membership. Closing/expiring recruitment affects applications only; project completion expires all pending requests.

Members is always available. Owner opens Lời mời → Mời thành viên, searches eligible friends, selects one, enters a role and sends. Return to the unified request list with status. Recipients can accept/reject on project detail. Existing invitation routes share the same rules.

Friend invitations collect only the position name, per the final requirement. Acceptance copies the role into ProjectMembers and preserves JoinRequestId for history. PositionId is reserved for posted recruitment positions; a direct invitation has no posted position. Community applications retain their complete recruitment data. No schema change is required.

Implementation order: regression tests → shared backend policy/legacy compatibility → single friend UI and recipient actions → targeted tests, backend build, frontend export and browser checks.
