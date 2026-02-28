FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj Workers/PvpAnalytics.Worker/
RUN dotnet restore Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj

COPY Workers/PvpAnalytics.Worker/ Workers/PvpAnalytics.Worker/
RUN dotnet publish Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PvpAnalytics.Worker.dll"]
