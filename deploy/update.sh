#!/usr/bin/env bash
# A-Fatura — update from GitHub: git pull → backup → rebuild → health check
# (pull first: it doesn't touch running containers, and .env may reference compose files that only exist in the new version)
set -euo pipefail
cd "$(dirname "$0")/.."

git pull --ff-only
set -a; source .env; set +a
./deploy/backup.sh
if [ "${BUILD_LOCAL:-false}" = "true" ]; then
  docker compose up -d --build            # compile on the VPS
else
  if [ -n "${GH_APP_ID:-}" ]; then ./deploy/ghcr-login.sh; fi   # GitHub App token (1h) → docker login ghcr.io
  docker compose pull                     # prebuilt images from GitHub Actions (ghcr.io)
  docker compose up -d --remove-orphans
fi

for _ in $(seq 1 60); do
  if curl -fs "http://127.0.0.1:${WEB_PORT:-8095}/api/health" >/dev/null; then
    echo "Atualizado: $(git log -1 --format='%h %s')"
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 3
done
echo "A API não respondeu depois da atualização. Ver: docker compose logs api"; exit 1
