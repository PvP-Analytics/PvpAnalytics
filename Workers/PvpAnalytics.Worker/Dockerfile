FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files for restore layering (Worker + referenced projects)
COPY Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj Workers/PvpAnalytics.Worker/
COPY Services/PvpAnalytics/PvpAnalytics.Application/PvpAnalytics.Application.csproj Services/PvpAnalytics/PvpAnalytics.Application/
COPY Services/PvpAnalytics/PvpAnalytics.Infrastructure/PvpAnalytics.Infrastructure.csproj Services/PvpAnalytics/PvpAnalytics.Infrastructure/
COPY Services/PvpAnalytics/PvpAnalytics.Core/PvpAnalytics.Core.csproj Services/PvpAnalytics/PvpAnalytics.Core/
COPY Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj Shared/PvpAnalytics.Shared/

RUN dotnet restore Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj

COPY . .
RUN dotnet publish Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj -c Release -o /app/publish --no-restore

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

RUN groupadd --gid 1000 worker \
    && useradd --uid 1000 --gid worker --no-create-home --shell /sbin/nologin worker \
    && chown -R worker:worker /app

USER worker
ENTRYPOINT ["dotnet", "PvpAnalytics.Worker.dll"]
