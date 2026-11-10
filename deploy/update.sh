#!/usr/bin/env bash
# A-Fatura — update from GitHub: backup → git pull → rebuild → health check
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a

./deploy/backup.sh
git pull --ff-only
docker compose up -d --build

for _ in $(seq 1 60); do
  if curl -fs "http://127.0.0.1:${WEB_PORT:-8095}/api/health" >/dev/null; then
    echo "Atualizado: $(git log -1 --format='%h %s')"
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 3
done
echo "A API não respondeu depois da atualização. Ver: docker compose logs api"; exit 1
