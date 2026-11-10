# ── Build ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AFatura.Api/AFatura.Api.csproj AFatura.Api/
RUN dotnet restore AFatura.Api/AFatura.Api.csproj
COPY AFatura.Api/ AFatura.Api/
RUN dotnet publish AFatura.Api/AFatura.Api.csproj -c Release -o /out /p:UseAppHost=false

# ── Runtime ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Claude CLI inside the container (it reuses the VPS login through the mounted ~/.claude volume).
# Set INSTALL_CLAUDE_CLI=false if you use Ai__Provider=Anthropic or None.
ARG INSTALL_CLAUDE_CLI=true
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && \
    if [ "$INSTALL_CLAUDE_CLI" = "true" ]; then \
      curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && \
      apt-get install -y --no-install-recommends nodejs && \
      npm install -g @anthropic-ai/claude-code ; \
    fi && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# Run as the same uid as the VPS user that owns ~/.claude (override with --build-arg APP_UID=$(id -u))
ARG APP_UID=1000
RUN (getent passwd $APP_UID >/dev/null || useradd -m -u $APP_UID afatura) && mkdir -p /data/files && chown -R $APP_UID /data
ENV HOME=/home/afatura
RUN mkdir -p /home/afatura && chown $APP_UID /home/afatura

COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD curl -fs http://localhost:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "AFatura.Api.dll"]
