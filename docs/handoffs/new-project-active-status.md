# New project initial status

- ProjectHubService.CreateAsync always persists Active (1), regardless of valid
  incoming Status 0/1/2. Completion timestamps remain null at creation.
- Legacy ProjectService.CreateProjectAsync also starts Active. Initial submission
  moderation is no longer invoked by creation; that path required Pending and
  could reject or revert the requested initial state. Other moderation flows remain.
- Existing projects and status updates are unaffected. No schema migration.
- Frontend CreateProjectModal already uses status 1 and has no status selector;
  no frontend implementation changes were needed.
- Regression: creation payloads 0 and 2 failed before the fix; all 3 now return
  and persist Active with null CompletedAt. ProjectHubTests, ProjectRecruitmentTests
  and SpecializationTests: 25 passed. Backend compilation passed. Client creation
  catalog browser checks passed, including the status-1 payload assertion.
- Legacy creation depends on PostgreSQL row locks and was not exercised against
  a live PostgreSQL instance in this change.
