# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KartShippingService.sln Directory.Build.props nuget.config ./
COPY packages/ packages/
COPY src/Api/Kart.Shipping.Api.csproj src/Api/
COPY src/Application/Kart.Shipping.Application.csproj src/Application/
COPY src/Domain/Kart.Shipping.Domain.csproj src/Domain/
COPY src/Infrastructure/Kart.Shipping.Infrastructure.csproj src/Infrastructure/
# The cache mount persists extracted NuGet packages under a stable id shared by every other
# kart-*-service Dockerfile, so restore stays fast (no re-download) even on a cache-miss here
# (e.g. after a .csproj change) as long as some other service's build already warmed it.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget-packages \
    dotnet restore src/Api/Kart.Shipping.Api.csproj

COPY src/ src/
COPY contracts/ contracts/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget-packages \
    dotnet publish src/Api/Kart.Shipping.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Kart.Shipping.Api.dll"]
