# UniNet backend

.NET 8 ASP.NET Core API with EF Core Code First and PostgreSQL.

## Local configuration

Set these environment variables or use .NET user secrets in `UniNet.API`:

- `ConnectionStrings__UniNet`: PostgreSQL connection string
- `Jwt__Key`: random signing key of at least 32 bytes
- `Google__ClientIds__0`: Google OAuth client ID accepted for ID tokens (add more numbered entries for each platform)
- `Cors__Origins__0`: web frontend origin, when using the Expo web app

Never commit credentials. The API fails at startup if the database connection or JWT key is missing. Google login returns `GOOGLE_NOT_CONFIGURED` until a client ID is configured.

The API reads the ignored `UniNet_BE/.env` file at startup (or `.env` in its content root), including nested keys using `__`. Local file values override appsettings/user secrets; explicit process environment and command-line values take precedence. EF design-time commands also read `ConnectionStrings__UniNet` from this file when the environment variable is not set.

## Verification meeting calendar

`UniNet.API/appsettings.json` supplies the meeting calendar: Vietnam time
(`Asia/Ho_Chi_Minh`), Monday–Saturday 08:00–17:00, 60-minute slots, capacity 1,
60 minutes minimum notice, a 30-day booking horizon and a 30-minute reservation hold.
Capacity is also limited by eligible active verificators who declared availability
covering the entire slot and have no conflicting commitments. No declared
availability means an empty slot list. See [availability APIs and deployment](docs/handoffs/verificator-availability.md).

Override these settings in deployment configuration or `.env`, for example
`ProjectVerification__TimeZoneId`, `ProjectVerification__WorkdayStart`,
`ProjectVerification__SlotMinutes` and `ProjectVerification__WorkingDays__0`
(use numbered entries for the remaining days). Restart/redeploy after configuration
changes. Missing or invalid calendar settings return `MEETING_POLICY_NOT_CONFIGURED`
(503); missing or invalid hold duration returns `RESERVATION_POLICY_NOT_CONFIGURED`.
The slot endpoint requires `from` and `to` with an explicit UTC offset and a range
of at most 31 days, for example `?from=2026-10-26T00:00:00Z&to=2026-10-31T00:00:00Z`.

## Commands

From the `UniNet_BE` directory:

```powershell
dotnet restore UniNet.sln
dotnet build UniNet.sln -m:1
dotnet test UniNet.Tests/UniNet.Tests.csproj -m:1
dotnet ef database update --project UniNet.Infrastructure --startup-project UniNet.Infrastructure --context UniNetDbContext
dotnet run --project UniNet.API
```

Swagger is available at `/swagger` in Development. The frontend `EXPO_PUBLIC_API_URL` must end with `/api`, for example `http://localhost:5000/api`. On an Android emulator, use the host address reachable from that emulator.

Roles, account statuses, and partner types are defined in `UniNet.Domain/Enums`. The database migration is in `UniNet.Infrastructure/Data/Migrations`. See `docs/handoffs/authentication.md` for the API contract and validation notes.

Project verification payment endpoints, PayOS configuration, webhook setup and migration notes are documented in [verification payments](docs/handoffs/verification-payments.md).
