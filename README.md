# UniNet backend

.NET 8 ASP.NET Core API with EF Core Code First and PostgreSQL.

## Local configuration

Set these environment variables or use .NET user secrets in `UniNet.API`:

- `ConnectionStrings__UniNet`: PostgreSQL connection string
- `Jwt__Key`: random signing key of at least 32 bytes
- `Google__ClientIds__0`: Google OAuth client ID accepted for ID tokens (add more numbered entries for each platform)
- `Cors__Origins__0`: web frontend origin, when using the Expo web app

Never commit credentials. The API fails at startup if the database connection or JWT key is missing. Google login returns `GOOGLE_NOT_CONFIGURED` until a client ID is configured.

EF design-time commands also read `ConnectionStrings__UniNet` from the ignored `UniNet_BE/.env` file when the environment variable is not set. Explicit environment configuration takes precedence. This local file loading applies to EF tooling; configure the API runtime through environment variables or user secrets as above.

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
