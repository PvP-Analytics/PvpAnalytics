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
RUN groupadd --gid 1000 daemon \
    && useradd --uid 1000 --gid daemon --no-create-home --shell /sbin/nologin daemon \
    && chown -R daemon:daemon /app
USER daemon
ENTRYPOINT ["dotnet", "PvpAnalytics.IngestionDaemon.dll"]
