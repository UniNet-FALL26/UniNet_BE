# Project timeline

- New Hub and legacy creation persist ProjectDetail.StartDate as creation day in
  UTC+7 and StartDateIsCreationDate=true. Owner can correct the date later.
- Only start and actual completion remain. UI calls ActualEndDate “Ngày kết thúc”.
  Planned EndDate/ExpectedOutput were removed from model/contracts and migration
  20261118060015_RemoveProjectExpectedEndDates drops both active DB columns.
  Exact legacy import end-date matches are preserved in ActualEndDate first.
- Applied 20261118060015_RemoveProjectExpectedEndDates to configured Neon neondb.
  Live information_schema check confirms StartDate/ActualEndDate remain and
  ProjectDetails.EndDate/Projects.ExpectedOutput are absent. Migration history
  confirms applied state. Restart/deploy updated API with the frontend contract.
- Validation: 43 logic/schema tests passed; 3 isolated PostgreSQL 18 migration
  tests passed (including full chain and confirmed import preservation); timeline
  and detail browser checks passed. Temporary PostgreSQL instance was removed.
- Owner-only PATCH /api/project-hub/{id} accepts `timeline` with nullable
  startDate/actualEndDate. Supplied timeline replaces these fields;
  omitted timeline preserves them. Owner may correct the creation-default start. End dates must
  not precede start; actual end cannot be future. Editing dates does not change status.
- Completion fills ActualEndDate only for automatic-start projects if empty.
  The flag is retained after correcting start so completion autofill still works.
  Completion rejects a start later than the actual completion day.
- Verified: 29 ProjectHubTests passed, including correction through timeline and
  details, completion timestamp preservation and invalid date-order rejection.
  Existing projects keep manually entered dates. Generic detail patches preserve
  actual date and enforce chronology.
- Migration 20261118030659_AddProjectTimelineDates adds nullable date and a boolean
  default false. Tested full migration chain and old-record preservation on an
  isolated PostgreSQL 18 instance. The later planned-date removal above is now
  applied to the configured application database.
- Apply with `dotnet ef database update --project UniNet.Infrastructure
  --startup-project UniNet.API`. Deploy backend and frontend together.
- Verification: 31 backend tests passed (ProjectHubTests, ProjectRecruitmentTests,
  SpecializationTests, ProjectTimelinePostgresTests). Model snapshot has no pending
  changes. Frontend create and project browser suites passed; web export passed.
- Frontend changes: ProjectDateField, CreateProjectModal, ProjectSettingsPanel,
  project details sidebar. See frontend handoff. No new dependencies.
