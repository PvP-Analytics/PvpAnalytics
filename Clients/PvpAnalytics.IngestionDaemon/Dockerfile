FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Clients/PvpAnalytics.IngestionDaemon/PvpAnalytics.IngestionDaemon.csproj Clients/PvpAnalytics.IngestionDaemon/
COPY Services/PvpAnalytics/ Services/PvpAnalytics/
COPY Shared/ Shared/
RUN dotnet restore Clients/PvpAnalytics.IngestionDaemon/PvpAnalytics.IngestionDaemon.csproj
COPY Clients/PvpAnalytics.IngestionDaemon/ Clients/PvpAnalytics.IngestionDaemon/
RUN dotnet publish Clients/PvpAnalytics.IngestionDaemon/PvpAnalytics.IngestionDaemon.csproj -c Release -o /app/publish --no-restore

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PvpAnalytics.IngestionDaemon.dll"]
