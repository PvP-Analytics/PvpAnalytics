FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8083

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Services/LoggingService/LoggingService.Api/LoggingService.Api.csproj", "Services/LoggingService/LoggingService.Api/"]
COPY ["Services/LoggingService/LoggingService.Application/LoggingService.Application.csproj", "Services/LoggingService/LoggingService.Application/"]
COPY ["Services/LoggingService/LoggingService.Infrastructure/LoggingService.Infrastructure.csproj", "Services/LoggingService/LoggingService.Infrastructure/"]
COPY ["Services/LoggingService/LoggingService.Core/LoggingService.Core.csproj", "Services/LoggingService/LoggingService.Core/"]
COPY ["Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj", "Shared/PvpAnalytics.Shared/"]
RUN dotnet restore "Services/LoggingService/LoggingService.Api/LoggingService.Api.csproj"
COPY . .
WORKDIR "/src/Services/LoggingService/LoggingService.Api"
RUN dotnet build "LoggingService.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "LoggingService.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LoggingService.Api.dll"]

