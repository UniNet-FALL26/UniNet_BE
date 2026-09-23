FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY UniNet.Domain/UniNet.Domain.csproj UniNet.Domain/
COPY UniNet.Infrastructure/UniNet.Infrastructure.csproj UniNet.Infrastructure/
COPY UniNet.Application/UniNet.Application.csproj UniNet.Application/
COPY UniNet.API/UniNet.API.csproj UniNet.API/
RUN dotnet restore UniNet.API/UniNet.API.csproj --disable-parallel

COPY . .
RUN dotnet publish UniNet.API/UniNet.API.csproj -c Release --no-restore -m:1 -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 10000
CMD ["sh", "-c", "exec dotnet UniNet.API.dll --urls http://0.0.0.0:${PORT:-10000}"]
