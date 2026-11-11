#!/usr/bin/env bash
# A-Fatura — first installation on the VPS (run from the repository root: ./deploy/install.sh)
# - creates .env from .env.example with generated secrets (only if .env does not exist yet)
# - builds and starts the Docker stack
# - waits for the API health check
set -euo pipefail
cd "$(dirname "$0")/.."
# Generic variables exported in the shell (e.g. after sourcing another project's .env) override .env in
# docker compose — never let them leak into this project.
unset IMAGE_PREFIX IMAGE_TAG COMPOSE_FILE COMPOSE_PROJECT_NAME

command -v docker >/dev/null || { echo "Docker não está instalado"; exit 1; }
docker compose version >/dev/null || { echo "Falta o plugin 'docker compose'"; exit 1; }

if [ ! -f .env ]; then
  cp .env.example .env
  set_env() { sed -i "s|^$1=.*|$1=$2|" .env; }
  set_env DB_PASSWORD "$(openssl rand -hex 24)"
  set_env JWT_KEY "$(openssl rand -hex 48)"
  set_env MAIL_WEBHOOK_SECRET "$(openssl rand -hex 24)"

  # Claude CLI login of the user who runs this script (or who called sudo)
  OWNER=${SUDO_USER:-$(id -un)}
  OWNER_HOME=$(getent passwd "$OWNER" | cut -d: -f6)
  set_env APP_UID "$(id -u "$OWNER")"
  set_env CLAUDE_HOME "$OWNER_HOME/.claude"
  set_env CLAUDE_JSON "$OWNER_HOME/.claude.json"

  echo ".env criado com segredos gerados."
  echo "Edita agora PUBLIC_HOST, ADMIN_EMAIL, ADMIN_PASSWORD (e email/IA se necessário):"
  echo "    nano .env"
  echo "Depois volta a correr ./deploy/install.sh"
  exit 0
fi

# shellcheck disable=SC1091
set -a; source .env; set +a

if [ "${AI_PROVIDER:-ClaudeCli}" = "ClaudeCli" ]; then
  [ -d "$CLAUDE_HOME" ] || { echo "Não encontrei $CLAUDE_HOME — faz 'claude login' no host ou usa AI_PROVIDER=Anthropic/None"; exit 1; }
  [ -f "$CLAUDE_JSON" ] || { echo "Não encontrei $CLAUDE_JSON (sem ele o Docker criaria uma pasta com esse nome)"; exit 1; }
fi
[ -n "${ADMIN_EMAIL:-}" ] && [ -n "${ADMIN_PASSWORD:-}" ] || echo "Aviso: ADMIN_EMAIL/ADMIN_PASSWORD vazios — não será criado o utilizador de backoffice."

if [ "${BUILD_LOCAL:-false}" = "true" ]; then
  docker compose up -d --build            # compile on the VPS
else
  ./deploy/ghcr-login.sh                  # docker login ghcr.io (GitHub App, gh CLI or existing login)
  docker compose pull                     # prebuilt images from GitHub Actions (ghcr.io)
  docker compose up -d --remove-orphans
fi

echo -n "A aguardar a API"
for _ in $(seq 1 60); do
  if curl -fs "http://127.0.0.1:${WEB_PORT:-8095}/api/health" >/dev/null; then
    echo " OK"
    echo "A-Fatura a correr em http://127.0.0.1:${WEB_PORT:-8095} — configura o reverse proxy para https://${PUBLIC_HOST}"
    exit 0
  fi
  echo -n "."; sleep 3
done
echo; echo "A API não respondeu. Ver: docker compose logs api"; exit 1
