FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["WebAPIDevSecOps/WebAPIDevSecOps.csproj", "WebAPIDevSecOps/"]
COPY ["UnitTest/UnitTest.csproj", "UnitTest/"]
COPY ["IntegrationTest/IntegrationTest.csproj", "IntegrationTest/"]
COPY ["SecurityTest/SecurityTest.csproj", "SecurityTest/"]
COPY ["DatabaseTest/DatabaseTest.csproj", "DatabaseTest/"]
COPY ["MutationTest/MutationTest.csproj", "MutationTest/"]
COPY ["ContractTest/ContractTest.csproj", "ContractTest/"]
COPY ["PerformanceTest/PerformanceTest.csproj", "PerformanceTest/"]
COPY ["WebAPIDevSecOps.slnx", "."]
RUN dotnet restore

COPY . .
RUN dotnet build -c Release --no-restore

FROM build AS publish
RUN dotnet publish WebAPIDevSecOps/WebAPIDevSecOps.csproj -c Release -o /app/publish --no-build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=publish /app/publish .

# Allow overriding the runtime user via build-arg. Default to UID 1000.
ARG APP_UID=1000
# Create a non-root user with the requested UID and make /app owned by it.
RUN set -eux; \
    if ! id -u appuser >/dev/null 2>&1; then \
      useradd -m -u "${APP_UID}" appuser || true; \
    fi; \
    chown -R "${APP_UID}":"${APP_UID}" /app || true

USER ${APP_UID}

ENTRYPOINT ["dotnet", "WebAPIDevSecOps.dll"]
