# syntax=docker/dockerfile:1
# Multi-stage build. Same image runs on Hetzner (Phase 1) and company AWS (Phase 3) —
# host swap is a redeploy, not a rewrite.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore against the csproj first for layer caching.
COPY src/TogglMcp/TogglMcp.csproj src/TogglMcp/
RUN dotnet restore src/TogglMcp/TogglMcp.csproj
COPY . .
RUN dotnet publish src/TogglMcp/TogglMcp.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Non-root. The aspnet image ships an 'app' user (uid 1654).
COPY --from=build /app .
# Durable state (mapping rules; Phase 3 token vault + Data Protection key ring).
RUN mkdir -p /data && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    STORAGE__DATADIR=/data \
    DOTNET_gcServer=0
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["/bin/sh", "-c", "wget -qO- http://127.0.0.1:8080/healthz || exit 1"]
ENTRYPOINT ["dotnet", "TogglMcp.dll"]
