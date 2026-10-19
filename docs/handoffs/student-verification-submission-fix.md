# Student verification submission failure

## Root cause and repair
The configured PostgreSQL database had applied `20261018003641_LinkVerificationToEvidenceModeration` but skipped `20261017152032_AddStudentVerificationNotifications`. `VerificationNotification.UserVerificationId` was absent and the target constraint only allowed project/evidence notifications. Saving a student request with an admin notification therefore failed in the submission transaction; the client displayed its generic API error.

Applied the missing migration to the configured database using `docs/migrations/student-verification-notifications.sql`. The transaction adds the nullable UUID column, index, restrictive foreign key and exactly-one-target check, then records the existing EF migration ID. Existing notification rows are preserved. The script skips an already-recorded migration and bounds lock/statement waits. No application redeployment or new frontend behavior is required.

## Verification
- Confirmed missing column/history and old check before repair; confirmed column, check, foreign key and migration history after commit.
- PostgreSQL regression fixture passes against temporary tables with full rollback: newer migration already recorded, existing notifications preserved, repeated application, student insertion, missing/multiple target rejection, invalid FK rejection and restricted deletion.
- Backend: 9 student verification tests pass, including real JWT/multipart HTTP submit, duplicate submissions, review and private document access, using isolated SQLite.
- Frontend: 5 verification rule/service tests pass; mocked browser checks pass at 320/440/1440px, including multipart submission, error retention, resubmission and pending status.
- No real student document or request was submitted as part of repair.

## Repeatable commands
With PostgreSQL connection environment variables configured:
```powershell
./UniNet.Tests/MigrationFixtures/RunMigrationFixture.ps1 -Fixture student-verification-notifications-assert.sql -PsqlPath 'C:/Program Files/PostgreSQL/18/bin/psql.exe'
```
```powershell
dotnet test UniNet.Tests/UniNet.Tests.csproj --no-restore --filter 'FullyQualifiedName~StudentVerification' --verbosity quiet -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```
