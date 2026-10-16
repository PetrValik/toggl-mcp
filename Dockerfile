# syntax=docker/dockerfile:1
# Multi-stage build. The same image runs anywhere Docker does, in either mode:
#   hosted HTTP (default):  docker run -p 127.0.0.1:8080:8080 --env-file .env toggl-mcp
#   local stdio:            docker run -i --rm -e TOGGL__APITOKEN -v toggl-mcp-data:/data toggl-mcp --stdio

# Base images pinned by digest for reproducible builds (bump deliberately — re-resolve a tag with
# `docker buildx imagetools inspect mcr.microsoft.com/dotnet/<repo>:10.0`).
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build
WORKDIR /src
# Restore against the csproj first for layer caching.
COPY src/TogglMcp/TogglMcp.csproj src/TogglMcp/
RUN dotnet restore src/TogglMcp/TogglMcp.csproj
COPY . .
RUN dotnet publish src/TogglMcp/TogglMcp.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS runtime
WORKDIR /app
# The Ubuntu-based aspnet image ships neither wget nor curl — the HEALTHCHECK below needs one.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
# Non-root. The aspnet image ships an 'app' user (uid 1654).
COPY --from=build /app .
# Durable state (mapping rules and the commit ledger).
RUN mkdir -p /data && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    STORAGE__DATADIR=/data \
    DOTNET_gcServer=0
EXPOSE 8080
VOLUME ["/data"]
# Under --stdio there is no HTTP listener, so Docker reports the container unhealthy. Harmless:
# the client owns that container (`docker run -i --rm`) and nothing restarts it on health.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:8080/healthz || exit 1"]
ENTRYPOINT ["dotnet", "TogglMcp.dll"]
