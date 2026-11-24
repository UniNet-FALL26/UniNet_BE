# UNINET — MAIN MERGE & NEON MIGRATION VERIFICATION

Date: 2026-11-20. **MERGE RESOLVED — NEON DEPLOYMENT BLOCKED.**

All eight unmerged paths are resolved and staged as resolutions. Current source builds, EF metadata is consistent, and the executed tests have no remaining failures. **Neon was not connected to, migrated, cloned or deployed.** Its project/branch/database, credentials source, writers and recovery capability have not been identified. Local disposable verification is not a substitute for a Neon data clone rehearsal.

## 1. Git state and preservation

| Item | Verified value |
|---|---|
| Branch | `dattt` |
| HEAD/current | `625718dd8ea3527fa3540e7660c558641c9f0aa2` |
| Actual MERGE_HEAD / origin/main | `c4f069ae25678f818f74840d625ad66f1f36a87b` |
| Actual merge base | `73997fee586ca96083cb632e2626679d8e1c0bd7` |
| Local main, older than actual incoming target | `eb849124a972a8a5bee491dcc42d5cc7a39db983` |
| HEAD/local-main merge base | `eb849124a972a8a5bee491dcc42d5cc7a39db983` |
| Tracking upstream for dattt | None configured |
| Operation | Merge, not rebase/cherry-pick |
| Final unmerged entries / conflict markers |0 /0 |
| Merge commit | **Not created**; MERGE_HEAD intentionally remains |

Before editing, porcelain status, index stages1/2/3 and each conflicted worktree file were saved outside Git. Initial incoming changes were already staged; they were preserved. The incoming commit, not the stale local `main` ref, was used to interpret theirs. No reset, clean, stash, blanket ours/theirs checkout, commit or push occurred.

### Semantic resolution decisions

| Conflicted file | Resolution |
|---|---|
| [ProjectInvitationService](../../UniNet.Application/Services/ProjectInvitationService.cs) | Keep main's accepted-friend invitations and unified Invite requests; retain effective-membership check. Do not expire legitimate friend invitations merely because recruitment ends. |
| [ProjectJoinRequestService](../../UniNet.Application/Services/ProjectJoinRequestService.cs) | Keep separate Apply/Invite filtering and new request model, plus Creator-inclusive effective-membership check. |
| [ProjectMemberService](../../UniNet.Application/Services/ProjectMemberService.cs) | Keep JSON recruitment/technology model and effective members; retain Git identity deactivation/history and manager-grant revocation on the existing departure path. Repository associations/history are not deleted. |
| [ProjectRoleCapacity](../../UniNet.Application/Services/ProjectRoleCapacity.cs) | Use new recruitment/request storage; count effective members with Creator once. Retain main's distinction between applications and friend invitations. |
| [ProjectTaskService](../../UniNet.Application/Services/ProjectTaskService.cs) | Keep main's explicit410 retired-feature contract. Old Task/report entities and tables were intentionally removed by main; retaining their old writers would query removed tables. Immutable V1 results and read endpoints remain. |
| [UniNetDbContext](../../UniNet.Infrastructure/Data/UniNetDbContext.cs) | Combine main's Project/Friendship/upload configurations with all OAuth/App/sync/evidence/snapshot/Peer/Official configurations and history guards. Remove only retired Workspace DbSets alongside main's retired entities. |
| [ModelSnapshot](../../UniNet.Infrastructure/Data/Migrations/UniNetDbContextModelSnapshot.cs) | Regenerate using EF's design-time scaffolder against the merged model, rather than selecting either side or concatenating blocks. Validate metadata and actual fresh schema. No historical migration or Designer was rewritten. |
| [PortfolioProjectReferenceTests](../../UniNet.Tests/PortfolioProjectReferenceTests.cs) | Keep main's logo/project-reference behavior and current Project fields; removed classification/planned-date properties are not reintroduced. |

Main's Project Hub, friendships, technology/recruitment JSON, timeline dates, profile visibility removal, upload metadata, notifications and related tests remain. Program retains new DI registrations and explicit-origin credentialed CORS alongside existing GitHub/Official DI and E2E worker isolation. JWT/auth middleware, Git weights, normalization, privacy/ownership rules, writer/checkpoint/fencing and immutable guards were not replaced.

## 2. Reproduced fixes after merge

### Real authorization defect

The existing regression `FormerMemberCannotReadVerifications(Active, LeftAt set)` initially failed: status-only effective membership admitted a departed member. [ProjectMembership](../../UniNet.Application/Common/ProjectMembership.cs) now requires Active **and LeftAt=null** for non-Creator membership queries. Creator inclusion remains unchanged. The test now passes; the full regression verifies dependent Git/Verification/workspace authorization.

### Test/setup compatibility corrections

- Two preserved Git/membership fixtures still initialized removed `ExpectedOutput`; that retired field was removed from fixture setup, not restored in the model.
- Real-host tests now locate the current API in isolated artifacts. The former test could fail to find the binary, and a misconfiguration test could otherwise pass merely because the assembly was missing.
- Partner tests now assert the actual existing `RegisterPartnerRequest` contract: retired personal name fields are JsonIgnore and organization identity is used. Nullable Skill Category test matches main's declared nullable model/check. No Auth production behavior was changed.
- The fake commit fixture lacks parent evidence. It must remain Incomplete; assertions now require that status and `CommitParents` reason rather than incorrectly demanding Complete. Production extraction/completeness was not relaxed.
- Historical migration fixtures used current EF INSERTs against old schemas, producing42703 for SessionVersion/ChatRoomId/assessment columns. [HistoricalFixtureSeed](../../UniNet.Tests/HistoricalFixtureSeed.cs) seeds only columns actually present in the deliberately old disposable schema, explicitly translates pre-StaffRoles role values, and accepts INSERTs only. It does not add fake schema columns, change migrations or remove constraints. Historical reads use required scalar projections instead of loading unrelated current fields/AutoIncludes.
- Migration round trips are bounded to the migration under test. They no longer try to downgrade all newer irreversible archive/Official migrations to test an older availability/notification change. The availability fixture now isolates the entire database because later migrations use a fixed archive schema name.
- [DockerPostgresFixtureTools](../../UniNet.Tests/DockerPostgresFixtureTools.cs) provides explicit disposable-container pg_dump/pg_restore when Windows PostgreSQL client binaries are unavailable. It verifies local host/port against Docker inspection, refuses55432/55433, transfers temporary custom dumps and removes only its own generated temporary file. Existing native-tool mode remains available.

These corrections keep assertions about archive contents, tampering rejection, unknown dependencies, rollback atomicity, original data and hashes. No immutable trigger, readiness rule or authorization assertion was disabled.

## 3. Build and tests

Latest unique outcomes: **1,130 PASS /0 FAIL /65 SKIP**, total **1,195 test identities**, including **157 PostgreSQL cases PASS**.

- The full Backend suite was executed with verified disposable settings, not just the prior267-test filter.
- Initial broad execution exposed7 non-Postgres/setup failures; enabling all portable PostgreSQL fixtures exposed historical-schema fixture errors. They were diagnosed and corrected as above.
- Latest full execution had1,127 PASS/3 FAIL/65 SKIP. Focused latest-source reruns passed all four selected cases, replacing the three failures and one already-passing snapshot case. The upgrade fixture was restored to its required88-migration baseline first; rerunning an upgrade test against its already-upgraded database is not a valid upgrade rehearsal.
- Final counts are merged by **TRX testId**, not truncated display names or number of executions. Two different Cloudinary theory inputs have identical truncated display text and are still counted separately. Reruns are not added as new tests.
- Official V2 service and Kestrel/JWT HTTP lifecycle, scoring/External/rounding, Peer privacy, evidenceV2–V5, snapshot coverage/replay/rollback, OAuth/App ABA/lease/hosted recovery, Creator membership, new Project Hub/friendship/timeline/refactor, Verification/Assessor/notifications/archive, portfolio/storage and ContributionV1 suites are included.
- Official creation/review/approval/finalize/publication and private member result reads were exercised only on disposable fixtures. No real-user or E2E result was finalized/published.
- Current isolated solution restore/build **PASS**,0 errors. Final build has one existing unread-controller-parameter warning; broader compile/test logs also show existing nullability/analyzer warnings.
- `HasPendingModelChanges=false`; `git diff --check` PASS. No code conflict markers or unmerged index stages remain.

### 65 tests not executed

Existing opt-in fixtures remain skipped, not reported as PASS:

| Fixture group | Skip count |
|---|---:|
| Git Activity E2E regression |1 |
| App activation |2 |
| App initial recovery |2 |
| App link HTTP/concurrency |4 |
| App older migration upgrade |8 |
| App reconciliation lifecycle/reconciliation |13 |
| App webhook consumer |24 |
| App webhook persistence/recovery |5 |
| App older worker fixture |5 |
| Separate UserRoleJson fixture |1 |

Several older App fixtures hardcode E2E port55433 and require separate enable flags; those flags were **not** enabled against a live target. The portable hosted worker/auth/ABA/scoped tests did run on54553. The separate UserRoleJson fixture requires its own connection opt-in. A future claim of complete optional-fixture coverage requires first making those older fixtures safely targetable; no tests were newly marked Skip to hide a failure. This task does not establish live Neon integration or every historical optional App fixture as verified.

## 4. Source migration inventory and schema reconciliation

All **103 source MigrationIds**, source blobs/SHA256, operation classes and risk flags are listed in [the inventory](../migrations/MergedSourceMigrationInventory.20261120.csv). It is a **source inventory, not a Neon pending list**. No duplicate ID with different source content between HEAD and incoming was found. Existing shared migration files/IDs/Designer files were not edited. The only resolved generated migration file is the aggregate model snapshot.

Nine migrations arrived from main:

```text
20261116023634_PrepareSimplifiedProjectSchema
20261116023712_BackfillSimplifiedProjectSchema
20261116030240_ArchiveAndRemoveLegacyProjectTables
20261116034415_AllowPersonalProjectLifecycle
20261117032216_AddFriendships
20261117092115_RemoveProjectClassification
20261118030659_AddProjectTimelineDates
20261118060015_RemoveProjectExpectedEndDates
20261119031147_RemoveCareerProfileVisibility
```

They are ordered by their real IDs with the existing evidence/Peer/Official migrations, not arbitrarily moved after them. The combined chain applies successfully from empty on disposable PostgreSQL. Current EF design model: **96 mapped tables,1,003 mapped columns**, every expected column/type/nullability matches the fully migrated database. Physical extras are the10 columns of migration-owned `GitHubAppAuthorizationFlowQuarantine`, intentionally outside normal EF entities; no missing/mismatched mapped column was found. Keys/FKs/checks/triggers are retained in catalog receipts and tested through real persistence/guards.

The fresh source chain contains103 migrations; **this is not evidence that Neon has any particular applied/pending count**. Neon history/schema cannot be reconciled without its actual target and read access. EF history stores IDs/product versions, not source checksums; an ID alone would not prove source-equivalent deployment.

### Operations requiring explicit live-data review

The inventory flags earlier destructive/data-transforming migrations as well as main's changes. If pending on Neon, specifically review:

| Migration/operation | Data impact / gate |
|---|---|
| ConsolidateVerificationPayments | Populate transaction VerificationId/reservation, drop old link table and PaymentUrl; reject unresolved checkout state. Verify all payment mappings before removal. |
| ArchiveAndRemoveLevel1 | Archive/remove moderation attempts/columns and references; validate preserved snapshots/FKs and unknown dependencies. |
| ArchiveAndRemoveVerificationEvidence | Archive then drop ProjectVerificationEvidence; verify archive checksums and no unexpected inbound dependency. |
| ArchiveAndRemoveExternalProjectFiles | Archive two file-link tables, alter draft processing state, drop links/lease fields; assess files/metadata and stopped writers. |
| Prepare/BackfillSimplifiedProjectSchema | New Project detail/recruitment/review structure; copy technologies, invitations, dates and request history; reconcile identities and invalid dates. |
| ArchiveAndRemoveLegacyProjectTables | Archive **11** Task/report/Workspace/invitation/skill/draft tables then remove them. New APIs intentionally retire these writers. No blanket full-migration approval is treated as approval for this business archival on unknown live data. |
| RemoveProjectClassification | Set internal submitter leadership, drop Origin. The source classification cannot be reconstructed by Down. |
| RemoveProjectExpectedEndDates | Preserve specifically confirmed imported dates, drop Projects.ExpectedOutput and ProjectDetails.EndDate. Other planned-date values are removed; explicit data disposition approval/export is needed if populated. |
| RemoveCareerProfileVisibility | Drop IsPublic; current main changes portfolio visibility semantics. Confirm intended privacy rollout and matching binary before live migration. |

Other historical catalog, identity/role, finance and notification transformations are included in the103-row inventory. Actual Neon pending transformations, affected row counts, duplicates/nullability/FK conflicts, extensions, locks and archive dependencies remain **NOT VERIFIED**. No seven-migration E2E assumption was reused.

### SQL generation hazard found

The ordinary concatenated EF script was tested on a fresh disposable and failed with **42601** at `20261024023526_AddProjectMinimumQuantity`: its raw UPDATE has no terminating semicolon before the next ALTER. EF's normal per-command execution succeeds because commands are separate protocol messages. This is a deployment-script packaging hazard; no applied migration source was rewritten.

A separate **fresh-disposable-only** script was generated from EF migration command boundaries, adding explicit command terminators and keeping one transaction/history insertion per actually executed migration. All103 transactions committed successfully on another fresh disposable. No commands required transaction suppression. Its SHA256 is `8564D0852E5B8466F714EE3C66A9CA065EAC38E52EF67BB2CC41DCDC346ED061`.

That file is non-idempotent rehearsal material, **not an approved Neon deployment script**. A real deployment must derive exactly the actual pending IDs, use independently reviewed guarded execution/command boundaries, record each durable outcome and handle uncertain commits before retry. Do not run the unreviewed full-chain concatenation or stamp history separately.

## 5. Disposable environment and migration evidence

- Container `uninet-main-merge-20261120`.
- ID `3c76c71414312adb88951267fb6efb7aaf3294c1988e3d1d56e53c51960aff8f`.
- Verified host127.0.0.1, actual port **54553**, rolepostgres, explicit disposable database names.
- Fresh EF chain:103 migrations; fresh command-boundary SQL chain:103 migrations.
- Required old evidence/auth/snapshot baselines separately prepared; successful upgrade preserves historical versions/observations and freezes membership.
- Project refactor fixture checks preserved source/archive data, request identity collision refusal/transaction rollback and immutable archive checksum.
- Verification/Level1/evidence archive fixtures check real custom-format dump→restore, original data, tampering, missing parents/archives and refusal of destructive/unsafe rollback.
- Official old-schema/legacy selection and finalized immutability tests pass. No destructive Down was used on live data.

This is **local disposable verification only**. No Neon branch clone, Neon recovery point, actual Neon data rehearsal or live smoke test was performed. Test containers are stopped at task completion; their evidence/data are retained.

## 6. Neon safety gate — blocked

Local `.env` and `.env.e2e` identify local127.0.0.1:55432, not Neon; the protected GitHub E2E configuration is a different local target. No verified Neon connection source/project/branch/database or control-plane capability was found among the available tools. A question requesting nonsecret target IDs/protected credential location was sent; no target has been supplied during this task.

Consequently these gates are not met:

1. Exact Neon project/branch/database/current role/version/schema.
2. Actual applied IDs, pending IDs and source-equivalent schema/data reconciliation.
3. Current sessions/deployed application/writer identity and quiescence plan.
4. Current consistent backup/recovery point and **actual restore/Neon clone** verification.
5. Existing business-data/archives/constraints/extension checks on that clone.
6. Explicit disposition approval for any pending destructive/archive transformation above.
7. Exact scoped reviewed script against the verified Neon baseline.
8. Authorized live read/auth smoke-test account and coherent application rollout.

No live DDL, history edits, backup overwrite, fake Peer/Git evidence, project creation, ratings, scoring/finalize/publish or test writes occurred on Neon. No Backend instance was started against an inferred target.

### Required continuation / execution order

1. Supply **nonsecret** Neon project/branch/database IDs and the path/name of the protected connection/control-plane credential source; do not paste tokens/passwords into chat.
2. Read-only target/history/catalog/count/session inventory. Classify every actual pending ID using the103-source inventory and inspect all data dependencies. Identify deployed writers/binary compatibility, including retired Project/portfolio contracts.
3. Establish a consistent recovery point; clone/restore the actual current Neon branch into an isolated branch. Verify data, extensions and migration history before rehearsal.
4. Review pending archive/delete/transform impact with actual affected IDs/counts. Obtain explicit approval if it exceeds the current safe additive deployment scope.
5. Generate/review the exact pending-only command-boundary script, checksums, timeouts and per-migration transaction/commit receipts. Rehearse on the clone, including writer/API compatibility, data reconciliation, failure rollback and relevant tests. Fresh empty-db success alone is insufficient.
6. Only after all gates pass: quiesce/coherently deploy, recheck no read-point drift, execute the approved plan, verify each committed ID/catalog/data change, and perform authorized non-destructive live API reads. Do not automatically restore over newer user work or run irreversible Down.

## 7. Runtime and Official V2 status

Existing local E2E **PID40756** was observed still running with its original start/executable; no E2E restart, login, migration, sync, capture or business-data write was performed in this merge task. The merged binary was built and tested in separate artifacts and **not deployed onto that older local E2E schema**. Its prior deployment report remains historical evidence, not a claim that the merged schema is installed there.

No Neon runtime/API/health/login check can be reported as PASS. Official V2's new-project default, RequiresOptIn/LegacyV1 preservation, separate60/40 Git/Peer allocation, full frozen roster, review/approve/reject/finalize/publish, member privacy, project isolation and immutable audit remain verified on disposable/current source. Task-based V1 recalculation remains retired by main; historical V1 reads/results remain available and were regression-tested.

## 8. Files and handoff

Changed during resolution: the eight conflict files above; [effective membership](../../UniNet.Application/Common/ProjectMembership.cs); test fixtures for Auth/model/real-host, Git processor/effective membership/snapshot upgrade and historical Verification/notification/availability migrations; the two new test-only helpers; this report, the source migration inventory and [next-agent handoff](UNINET_GITHUB_CONTRIBUTION_NEXT_AGENT.md). Existing incoming files remain staged; subsequent fixes/documentation remain reviewable in the working tree. No source migration/Designer rewrite, corrective schema migration or scoring policy change was needed.

Protected receipts: `%LOCALAPPDATA%/UniNetMergeAudit/20261120-054058/` contains initial status/index-stage copies,103-ID/source-hash inventory, EF-generated snapshot/model proof, migration/schema/catalog/script receipts, failed and successful diagnostic logs, TRX/latest testId outcomes, skipped-test list and final build/diff/status evidence. No passwords/JWT/connection secrets are included in this report.

**Final status: MERGE RESOLVED — NEON DEPLOYMENT BLOCKED.**

The merge is still awaiting a human commit by design. Do not describe Neon as migrated/tested, rerun blanket migrations, reuse E2E applied counts for Neon, or enable live writers until the specific remaining gates above are satisfied.
