# Build context is the repository root (see docker-compose.yml), because this
# project also needs Trackr.Shared and the Directory.*.props files.
#
#   docker build -f src/Trackr.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project files alone so the (slow) restore layer stays cached
# when only source changes. Configuration=Release matters here: Trackr.Api.csproj
# references Trackr.Client in Debug only, and that project is not in this context.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Trackr.Api/Trackr.Api.csproj src/Trackr.Api/
COPY src/Trackr.Shared/Trackr.Shared.csproj src/Trackr.Shared/
RUN dotnet restore src/Trackr.Api/Trackr.Api.csproj -p:Configuration=Release

COPY src/Trackr.Api/ src/Trackr.Api/
COPY src/Trackr.Shared/ src/Trackr.Shared/
RUN dotnet publish src/Trackr.Api/Trackr.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

COPY --from=build /app/publish .

# Plain HTTP: TLS is terminated by nginx and by the user's reverse proxy upstream.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Liveness, not readiness - the backend should not be restarted just because
# Postgres is briefly unavailable. busybox wget stands in for curl on alpine.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD wget --spider -q http://127.0.0.1:8080/api/health/live || exit 1

# APP_UID is a non-root user baked into the Microsoft base images.
USER $APP_UID

ENTRYPOINT ["dotnet", "Trackr.Api.dll"]
