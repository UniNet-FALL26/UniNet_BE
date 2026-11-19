# Student friendships

- 2026-11-18: FriendPerson adds MutualFriendsCount (JSON mutualFriendsCount) for
  the two-tab friends redesign. SQL projection counts active Student profiles
  accepted by both viewer and target, excluding endpoints; self returns zero.
  Covers lists, relationship reads and mutation responses without schema changes.
  Twenty SQLite friendship cases passed, including both edge directions, all
  nonaccepted states, inactive/nonstudent mutuals and mutation/count consistency.

- Contract/design: [../friendships.md](../friendships.md).
- New Friendship entity/configuration/service/controller. Account IDs identify
  both participants; profile IDs identify public-profile lookups and project
  invitees. Only active Student accounts may act or appear in candidate results.
- One unordered pair per row via unique stored computed low/high GUID columns.
  States: pending, accepted, rejected, cancelled, removed. Re-request reuses row
  with incremented Version; every decision checks the client Version under the
  PostgreSQL pair advisory lock. Reverse pending sends return 409.
- Reuses Notifications and NotificationDeliveries outbox in the same transaction.
  Friendship target resolves to the student-only `/profile/friends` route.
- Project candidate filters require owner and exclude creator, active members,
  pending project invitees and non-students. Existing ProjectHub invitation and
  decision endpoints remain authoritative; friend acceptance never adds members.
- Migration `20261117032216_AddFriendships`, designer and model snapshot included.
  Full migration chain successfully applied to an isolated PostgreSQL 18 test
  database. Shared/application database was not migrated.
- Verification: 60 focused/regression tests passing (SQLite, PostgreSQL race and
  rollback tests, real HTTP authorization and DTO binding, project/notifications).
  Additional NotificationsHttpTests suite: 12 passing. Backend solution build and
  frontend web export passed. Frontend typecheck has pre-existing errors outside
  this feature; see frontend handoff for exact files.
- Apply migration in the intended environment with
  `dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.API`.
- Test command: set UNINET_TEST_POSTGRES to an isolated test server and run
  `dotnet test UniNet.Tests --no-restore -m:1 --filter FullyQualifiedName~Friendship`.
  The temporary local test server used during implementation has been stopped.
