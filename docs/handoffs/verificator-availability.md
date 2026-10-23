# Verificator availability

Availability consists of explicit dated intervals, not a recurring weekly calendar.
Only an active Verificator may manage their own intervals. Use their access token;
the owner comes from the authenticated account, never a request user ID.

| Method | URL | Result |
| --- | --- | --- |
| GET | `/api/verificators/me/availability?from=...&to=...` | 200, array of overlapping intervals |
| POST | `/api/verificators/me/availability` | 201, created interval |
| PUT | `/api/verificators/me/availability/{id}` | 200, updated interval |
| DELETE | `/api/verificators/me/availability/{id}` | 204 |

POST and PUT accept:

```json
{
  "startAt": "2026-10-26T08:00:00+07:00",
  "endAt": "2026-10-26T17:00:00+07:00"
}
```

Responses contain `id`, `startAt`, `endAt`; stored and returned times are UTC.
Send explicit offsets and URL-encode `+` in query parameters. GET requires both
`from` and `to`, with `to > from` and a range of at most 31 days. Intervals must
start in the future, last at most 31 days and end within 365 days. Overlapping
intervals for the same person are rejected; adjacent intervals are allowed.
A meeting must fit entirely inside one interval. Enter one continuous interval
for a continuous period of availability; split intervals to represent breaks.

## Booking and assignment

- An active account alone no longer supplies meeting capacity. Without declared
  availability, a Verificator cannot receive a new meeting assignment.
- The project meeting policy still defines working days, hours, duration, notice,
  booking horizon and the global concurrency cap. Personal availability only
  narrows these slots.
- `meeting-slots` only counts eligible available people, excluding the requester,
  project leader, project members, other active holds/confirmed reservations and
  conflicting active assignments (including meetings still in progress).
- A hold reserves one eligible person's capacity internally. This is not an
  assignment and does not grant meeting access. The reservation expires/cancels
  with the existing booking lifecycle. Payment rechecks capacity and availability.
- Admin availability, assignment and replacement use the same eligibility rules.
  Admin can pick another free eligible person; assignment updates the reserved
  resource. The existing post-meeting overdue-report replacement job remains a
  report recovery workflow, not a new meeting booking.
- Updates/deletes cannot remove coverage needed by a live hold, confirmed booking
  or active assignment. Extensions that retain coverage are allowed. Expired and
  cancelled holds do not block calendar changes.
- Writes use Serializable transactions. Retry after a 409 conflict by refreshing
  availability and slots.

## Errors

| HTTP | Code | Meaning |
| --- | --- | --- |
| 400 | `INVALID_AVAILABILITY_RANGE` | Invalid GET range |
| 400 | `INVALID_AVAILABILITY_INTERVAL` | Invalid POST/PUT interval |
| 403 | `FORBIDDEN` | Account is not an active Verificator |
| 404 | `AVAILABILITY_NOT_FOUND` | Missing interval or owned by another person |
| 409 | `AVAILABILITY_OVERLAP` | Personal intervals overlap |
| 409 | `AVAILABILITY_IN_USE` | Would invalidate a meeting commitment |
| 409 | `AVAILABILITY_CONFLICT` | Concurrent calendar update; reload and retry |

Existing booking errors (`MEETING_SLOT_UNAVAILABLE`, `VERIFICATION_CONFLICT`) and
assignment errors (`VERIFICATOR_UNAVAILABLE`, `ASSIGNMENT_CONFLICT`) remain in use.

## Deployment

Apply migration `20261023021306_AddVerificatorAvailability` before starting the
updated API:

```powershell
dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.Infrastructure --context UniNetDbContext
```

The migration adds the availability table and a nullable reserved-person foreign
key, and backfills that key from existing active assignments. It creates no
availability and does not change booking/payment state. Existing unassigned
bookings without a named resource conservatively consume capacity until assigned;
calendar changes overlapping those commitments are protected. Existing staff
must declare their actual availability before new slots can be booked/assigned.
