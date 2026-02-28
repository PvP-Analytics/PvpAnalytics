FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for restore layering (maintaining directory structure)
# Order matters: copy dependencies first
COPY Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj Shared/PvpAnalytics.Shared/
COPY Services/AuthService/AuthService.Core/AuthService.Core.csproj Services/AuthService/AuthService.Core/
COPY Services/AuthService/AuthService.Application/AuthService.Application.csproj Services/AuthService/AuthService.Application/
COPY Services/AuthService/AuthService.Infrastructure/AuthService.Infrastructure.csproj Services/AuthService/AuthService.Infrastructure/
COPY Services/AuthService/AuthService.Api/AuthService.Api.csproj Services/AuthService/AuthService.Api/

# Restore projects in dependency order to ensure all dependencies are available
# This helps isolate any restore issues to specific projects
RUN dotnet restore Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj && \
    dotnet restore Services/AuthService/AuthService.Core/AuthService.Core.csproj && \
    dotnet restore Services/AuthService/AuthService.Application/AuthService.Application.csproj && \
    dotnet restore Services/AuthService/AuthService.Infrastructure/AuthService.Infrastructure.csproj && \
    dotnet restore Services/AuthService/AuthService.Api/AuthService.Api.csproj

COPY . .
WORKDIR /src/Services/AuthService/AuthService.Api
RUN dotnet build AuthService.Api.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish AuthService.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AuthService.Api.dll"]

