# Public details and independent friend invitations

- Project detail is readable by active students regardless of legacy Visibility. Owner/member mutation and workspace boundaries remain independent. Hub writes/read DTOs normalize public; privacy UI removed. Legacy column retained; no migration required.
- Applications require open, unexpired recruitment and available posted positions.
- Owner can invite an accepted active student friend into a Pending, Active or Completed project without recruitment. Rejected projects remain blocked. Required role <=150 characters. ProjectJoinRequests stores pending invite; recipient acceptance alone creates/reactivates ProjectMembers with Role and JoinRequestId. Friendship acceptance never creates project members.
- Legacy invitation service preserves response/error/notification contracts and applies the same friend/state policy. Direct invitations neither set recruitment Full nor trigger application-capacity cleanup. Shared role cleanup preserves pending invitations except when recipient already joined.
- Recruitment close/deadline and project completion expire applications only and preserve pending friend invitations. Acceptance preserves completed status and completion timestamp. Project classification was removed; see remove-project-classification.md. Existing notification outbox retained.
- GET requests/mine supports optional projectId, preserving participant filtering before the project filter and bounded ordering.
- FE members tab always available. Owner: Lời mời → Mời thành viên → select one eligible friend → enter position → send → unified list/status. Recipient detail card accepts/rejects. No message/full recruitment fields for friend invites.
- Tests cover old private detail visibility without edit/membership, independent invitations, receiver authority, role persistence, inbox scope, closure/deadline survival and completion. Browser suites cover sender/recipient, mobile, privacy removal and existing project editors.
