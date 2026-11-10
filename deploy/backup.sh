#!/usr/bin/env bash
# Daily backup: database dump + documents volume. Cron: 30 3 * * * /opt/a-fatura/deploy/backup.sh
set -euo pipefail
cd "$(dirname "$0")/.."
DEST=${BACKUP_DIR:-/var/backups/afatura}
STAMP=$(date +%Y%m%d-%H%M)
mkdir -p "$DEST"

docker compose exec -T db pg_dump -U afatura -Fc afatura > "$DEST/db-$STAMP.dump"
docker run --rm -v afatura_files:/data:ro -v "$DEST":/backup alpine tar czf "/backup/files-$STAMP.tgz" -C /data .

# keep 14 days
find "$DEST" -type f -mtime +14 -delete
echo "Backup OK → $DEST ($STAMP)"
