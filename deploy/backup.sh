#!/usr/bin/env bash
# Daily backup: database dump + documents volume. Cron: 30 3 * * * /opt/a-fatura/deploy/backup.sh
set -euo pipefail
cd "$(dirname "$0")/.."
DEST=${BACKUP_DIR:-/var/backups/afatura}
STAMP=$(date +%Y%m%d-%H%M)
mkdir -p "$DEST"

# The database must be running for pg_dump (e.g. after `docker compose down`): start it and wait until ready.
if [ -z "$(docker compose ps -q --status running db 2>/dev/null)" ]; then
  echo "A base de dados não está a correr — a arrancar o serviço db…"
  docker compose up -d db
  for _ in $(seq 1 30); do docker compose exec -T db pg_isready >/dev/null 2>&1 && break; sleep 2; done
fi

docker compose exec -T db pg_dump -U afatura -Fc afatura > "$DEST/db-$STAMP.dump"
docker run --rm -v afatura_files:/data:ro -v "$DEST":/backup alpine tar czf "/backup/files-$STAMP.tgz" -C /data .

# keep 14 days
find "$DEST" -type f -mtime +14 -delete
echo "Backup OK → $DEST ($STAMP)"
