FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["BMSL_Tracker/BMSL_Tracker.csproj", "BMSL_Tracker/"]
RUN dotnet restore "BMSL_Tracker/BMSL_Tracker.csproj"

COPY . .
WORKDIR /src/BMSL_Tracker
RUN dotnet publish "BMSL_Tracker.csproj" --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeyRingPath=/var/data-protection
EXPOSE 8080

RUN mkdir -p /var/data-protection && chown "$APP_UID:$APP_UID" /var/data-protection
USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BMSL_Tracker.dll"]
