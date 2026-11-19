# Public project details and direct friend invitations

Authenticated active students may read all project details. Reading never grants edit or workspace membership. Remove privacy controls; retain the legacy visibility column/contract for compatibility and normalize Hub writes to public.

Applications require open, unexpired recruitment and an available posted position. Direct invitations require the project owner, a Pending/Active/Completed project, an accepted student friend and a required role (150 characters maximum). They do not require recruitment or reserve a posted position. Only recipient acceptance creates membership. Closing/expiring recruitment and project completion affect applications only and preserve pending invitations.

Members is always available. Owner opens Lời mời → Mời thành viên, searches eligible friends, selects one, enters a role and sends. Return to the unified request list with status. Recipients can accept/reject on project detail. Existing invitation routes share the same rules.

Friend invitations collect only the position name, per the final requirement. Acceptance copies the role into ProjectMembers and preserves JoinRequestId for history. PositionId is reserved for posted recruitment positions; a direct invitation has no posted position. Community applications retain their complete recruitment data. No schema change is required.

All Completed projects may invite accepted student friends. Acceptance must retain completed status and completion timestamp. Rejected projects remain ineligible. Completion or an edit to an already completed project must preserve pending friend invitations. Recruitment/application rules remain unchanged. Apply this policy in Hub and legacy APIs, sender UI and recipient UI; verify the status matrix, authorization and role persistence. Origin and Mode were removed; see remove-project-classification.md.

Implementation order: regression tests → shared backend policy/legacy compatibility → single friend UI and recipient actions → targeted tests, backend build, frontend export and browser checks.
