# Project timeline

- New Hub and legacy creation persist ProjectDetail.StartDate as creation day in
  UTC+7 and StartDateIsCreationDate=true. Optional expected end date comes from
  CreateHubProjectRequest.ExpectedEndDate (legacy ExpectedOutput). Never backfill.
- Reuse EndDate for expected completion. ActualEndDate is separate calendar
  metadata; Project.CompletedAt remains the system lifecycle timestamp.
- Owner-only PATCH /api/project-hub/{id} accepts `timeline` with nullable
  startDate/expectedEndDate/actualEndDate. Supplied timeline replaces these fields;
  omitted timeline preserves them. Automatic start cannot change. End dates must
  not precede start; actual end cannot be future. Editing dates does not change status.
- Completion fills ActualEndDate only for automatic-start projects if empty.
  Existing projects keep manually entered dates. Generic detail patches preserve
  actual date and enforce locked start/chronology.
- Migration 20261118030659_AddProjectTimelineDates adds nullable date and a boolean
  default false. Tested full migration chain and old-record preservation on an
  isolated PostgreSQL 18 instance. Application/shared database was not migrated.
- Apply with `dotnet ef database update --project UniNet.Infrastructure
  --startup-project UniNet.API`. Deploy backend and frontend together.
- Verification: 31 backend tests passed (ProjectHubTests, ProjectRecruitmentTests,
  SpecializationTests, ProjectTimelinePostgresTests). Model snapshot has no pending
  changes. Frontend create and project browser suites passed; web export passed.
- Frontend changes: ProjectDateField, CreateProjectModal, ProjectSettingsPanel,
  project details sidebar. See frontend handoff. No new dependencies.
