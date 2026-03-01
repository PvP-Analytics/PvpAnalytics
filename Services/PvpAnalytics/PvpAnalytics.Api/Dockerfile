FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files for restore layering
COPY Services/PvpAnalytics/PvpAnalytics.Api/PvpAnalytics.Api.csproj Services/PvpAnalytics/PvpAnalytics.Api/
COPY Services/PvpAnalytics/PvpAnalytics.Application/PvpAnalytics.Application.csproj Services/PvpAnalytics/PvpAnalytics.Application/
COPY Services/PvpAnalytics/PvpAnalytics.Infrastructure/PvpAnalytics.Infrastructure.csproj Services/PvpAnalytics/PvpAnalytics.Infrastructure/
COPY Services/PvpAnalytics/PvpAnalytics.Core/PvpAnalytics.Core.csproj Services/PvpAnalytics/PvpAnalytics.Core/
COPY Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj Shared/PvpAnalytics.Shared/

RUN dotnet restore "Services/PvpAnalytics/PvpAnalytics.Api/PvpAnalytics.Api.csproj"

# Copy the full source and build
COPY . .
WORKDIR /src/Services/PvpAnalytics/PvpAnalytics.Api
RUN dotnet build "PvpAnalytics.Api.csproj" -c "$BUILD_CONFIGURATION" -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PvpAnalytics.Api.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PvpAnalytics.Api.dll"]
