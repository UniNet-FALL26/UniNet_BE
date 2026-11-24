# Phase 2 — real peer submissions for A+B

## Closed E2E cohort — do not submit again

On2026-11-20 the user explicitly authorized automation under their two E2E accounts with A→B4/B→A5, commentnull. Both official first submissions persisted; cohort is **Closed**, PeerSnapshot `c6fe092b-ea1f-4e19-b04e-384a7663424b`, V2 Draft Run#4 Ready. See [execution report](UNINET_AUTOMATED_PEER_EVALUATION_CONTRIBUTION_V2_20261120.md). The instructions below are retained as the original submission contract, not an instruction to resubmit/edit/reopen this completed cohort. These were user-selected E2E test ratings, not independent production feedback.

## Current cohort

- API: `http://localhost:5239`
- Project: `68f98a9f-d73d-4dcf-be5c-7099b29329c0`
- Cohort: **`5f798512-6bce-4c66-b58d-c3d5b859fae7`**
- Frozen roster: Creator Student A (`fef4ef4d-0049-4f7a-b629-fe9760bbbff9`) and Student B (`8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`).
- Opened through Creator A's authenticated API request. At handoff: **Open, 0/2 submissions, no Peer Snapshot**. No agent rating or close request was sent.

Each member must choose and send their own rating. Do not send a password/JWT or private comment to the agent. Use your own browser/session and the existing Auth API / Swagger at `http://localhost:5239/swagger/index.html`. Authenticate your own account, then use its bearer authorization; do not reuse the other member's token. Login does not submit a rating.

## Read progress first

`GET /api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/contribution/git-experimental/peer-cohorts/5f798512-6bce-4c66-b58d-c3d5b859fae7`

Both participants can read cohort progress. An ordinary member sees only their own outgoing rating, never the incoming rating/comment or revision history. Current policy allows the managing Creator to inspect ratings/history. With two participants, inference anonymity cannot be guaranteed.

## Student A evaluates Student B

Authenticate as `student-a@uninet-e2e.test`, then:

`PUT /api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/contribution/git-experimental/peer-cohorts/5f798512-6bce-4c66-b58d-c3d5b859fae7/evaluations/8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`

## Student B evaluates Student A

Authenticate as `student-b@uninet-e2e.test`, then:

`PUT /api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/contribution/git-experimental/peer-cohorts/5f798512-6bce-4c66-b58d-c3d5b859fae7/evaluations/fef4ef4d-0049-4f7a-b629-fe9760bbbff9`

## Body contract — choose your own score

| JSON field | Value |
|---|---|
| `score` | Your own integer rating, 1 through 5; no suggested/default rating |
| `comment` | Your own optional comment, at most 2,000 characters; null is allowed |
| `expectedRevision` | null on first submission; current outgoing rating's `revision` when editing |

Set `Content-Type: application/json`. The evaluator is derived from the authenticated JWT: there is no request field for impersonating another evaluator. Never put your own profile ID in the evaluatee URL. Do not submit a score just to achieve a desired contribution percentage.

Read the response and GET progress again. A successful first submission creates one current pair and revision 1. The other participant still has to submit independently. Duplicate/stale requests return `PEER_REVISION_CONFLICT` (409); reload your outgoing rating and use its current revision before an intentional edit. Editing is supported only while not Closed; history is retained. Do not open a new cohort to retry a rating.

## Completion and closure

After one real submission the cohort is Collecting; after both reciprocal submissions it becomes ReadyToClose. It does **not** auto-close. Missing ratings are Pending, not PeerScore=0.

Only the managing Creator may close a complete cohort, using:

`POST /api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/contribution/git-experimental/peer-cohorts/5f798512-6bce-4c66-b58d-c3d5b859fae7/close`

**Not called by this task.** Before closure, verify two genuinely member-submitted ratings, the unchanged active roster, and revisions. Closure freezes the Peer Snapshot; do not close for a dry run or fabricate missing ratings. Report completion so the authorized Phase 2 flow can verify and continue. No finalize/publication is authorized.

Git remains Incomplete on snapshot `467894ae-e5ed-41c9-af7f-9095e581e5a4`; closing Peer alone will not make a V2 calculation Ready. Do not invoke Run creation, normalization publication or finalize as part of these submission instructions. Real received ratings are converted by the existing policy; all minimum ratings can yield zero peer mass and InsufficientData, rather than invented positive points or redistribution.
