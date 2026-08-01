# Build context is the repository root (see docker/docker-compose.yml), because this
# project also needs Trackr.Shared and the Directory.*.props files.
#
#   docker build -f src/Trackr.Web/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Trackr.Web/Trackr.Web.csproj src/Trackr.Web/
COPY src/Trackr.Shared/Trackr.Shared.csproj src/Trackr.Shared/
RUN dotnet restore src/Trackr.Web/Trackr.Web.csproj -p:Configuration=Release

COPY src/Trackr.Web/ src/Trackr.Web/
COPY src/Trackr.Shared/ src/Trackr.Shared/
RUN dotnet publish src/Trackr.Web/Trackr.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


# A Blazor WebAssembly app is just static files once published; nginx serves them
# and reverse-proxies /api/ to the backend so the whole app is one origin.
FROM nginx:1.29-alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/Trackr.Web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --spider -q http://127.0.0.1/healthz || exit 1
