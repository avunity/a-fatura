#!/usr/bin/env bash
# docker login ghcr.io before pulling the images. Tries, in order:
#   1. GitHub App (GH_APP_ID + GH_APP_KEY in .env) — short-lived installation token
#   2. gh CLI already authenticated on the VPS (`gh auth token`) — needs the read:packages scope:
#        gh auth refresh -h github.com -s read:packages
#   3. nothing → keeps whatever `docker login ghcr.io` was done before (e.g. a PAT)
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a

if [ -z "${GH_APP_ID:-}" ]; then
  if command -v gh >/dev/null && gh auth status -h github.com >/dev/null 2>&1; then
    USER_LOGIN=$(gh api user --jq .login 2>/dev/null || echo x-access-token)
    if gh auth token -h github.com | docker login ghcr.io -u "$USER_LOGIN" --password-stdin >/dev/null; then
      echo "ghcr.io: login com o gh CLI ($USER_LOGIN) OK"; exit 0
    fi
    echo "ghcr.io recusou o token do gh — corre: gh auth refresh -h github.com -s read:packages"; exit 1
  fi
  echo "ghcr.io: sem GitHub App nem gh CLI — a usar o docker login existente"; exit 0
fi
: "${GH_APP_KEY:?GH_APP_KEY (caminho da chave .pem) não definido no .env}"
REPO=${GH_REPO:-avunity/a-fatura}
API=https://api.github.com

b64() { base64 -w0 | tr '+/' '-_' | tr -d '='; }
now=$(date +%s)
header=$(printf '{"alg":"RS256","typ":"JWT"}' | b64)
payload=$(printf '{"iat":%d,"exp":%d,"iss":"%s"}' $((now - 60)) $((now + 540)) "$GH_APP_ID" | b64)
signature=$(printf '%s.%s' "$header" "$payload" | openssl dgst -sha256 -sign "$GH_APP_KEY" -binary | b64)
JWT="$header.$payload.$signature"

gh_api() { curl -fsS -H "Authorization: Bearer $JWT" -H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28" "$@"; }

INSTALLATION_ID=${GH_APP_INSTALLATION_ID:-}
if [ -z "$INSTALLATION_ID" ]; then
  INSTALLATION_ID=$(gh_api "$API/repos/$REPO/installation" | grep -o '"id": *[0-9]*' | head -1 | grep -o '[0-9]*$')
fi

TOKEN=$(gh_api -X POST "$API/app/installations/$INSTALLATION_ID/access_tokens" | sed -n 's/.*"token": *"\([^"]*\)".*/\1/p')
[ -n "$TOKEN" ] || { echo "Não foi possível obter o token da GitHub App"; exit 1; }

echo "$TOKEN" | docker login ghcr.io -u x-access-token --password-stdin >/dev/null \
  && echo "ghcr.io: login com a GitHub App (instalação $INSTALLATION_ID) OK" \
  || { echo "ghcr.io recusou o token da GitHub App — usa um PAT classic com read:packages (docs/CI-CD.md)"; exit 1; }
