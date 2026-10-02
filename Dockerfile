# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# BMSL Tracker — production image (multi-stage, non-root, health-checked)
#
#   docker build -t bmsl-tracker .
#   docker run -d -p 8080:8080 \
#     -e ConnectionStrings__DefaultConnection="Server=db;Database=BMSL_Tracker;User=sa;Password=...;TrustServerCertificate=True;Encrypt=True" \
#     -e Database__ApplyMigrations=true \
#     bmsl-tracker
#
# For the full stack (app + SQL Server) use docker-compose.yml.
# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first for better layer caching
COPY global.json Directory.Build.props ./
COPY BMSL_Tracker/BMSL_Tracker.csproj BMSL_Tracker/
COPY tests/BMSL_Tracker.Tests/BMSL_Tracker.Tests.csproj tests/BMSL_Tracker.Tests/
RUN dotnet restore BMSL_Tracker/BMSL_Tracker.csproj --disable-parallel

COPY . .
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish BMSL_Tracker/BMSL_Tracker.csproj \
      -c "$BUILD_CONFIGURATION" \
      -o /app/publish \
      /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl only for the container health check
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Writable locations for non-root runs (Data Protection key ring volume inherits these perms).
RUN mkdir -p /keys && chown -R $APP_UID /keys && chmod 700 /keys

# Non-root; ASP.NET images provide $APP_UID
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "BMSL_Tracker.dll"]
