FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files for restore layering
COPY Services/PaymentService/PaymentService.Api/PaymentService.Api.csproj Services/PaymentService/PaymentService.Api/
COPY Services/PaymentService/PaymentService.Application/PaymentService.Application.csproj Services/PaymentService/PaymentService.Application/
COPY Services/PaymentService/PaymentService.Infrastructure/PaymentService.Infrastructure.csproj Services/PaymentService/PaymentService.Infrastructure/
COPY Services/PaymentService/PaymentService.Core/PaymentService.Core.csproj Services/PaymentService/PaymentService.Core/
COPY Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj Shared/PvpAnalytics.Shared/

RUN dotnet restore "Services/PaymentService/PaymentService.Api/PaymentService.Api.csproj"

# Copy the full source and build
COPY . .
WORKDIR /src/Services/PaymentService/PaymentService.Api
RUN dotnet build "PaymentService.Api.csproj" -c "$BUILD_CONFIGURATION" -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaymentService.Api.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaymentService.Api.dll"]

