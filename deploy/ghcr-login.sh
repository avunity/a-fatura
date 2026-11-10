#!/usr/bin/env bash
# docker login ghcr.io with a short-lived GitHub App installation token (valid 1h — regenerated on every update).
# Needs in .env: GH_APP_ID and GH_APP_KEY (path to the app private key .pem). GH_APP_INSTALLATION_ID is optional
# (discovered from the repository). The app needs "Packages: Read" and access to the repository.
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a

: "${GH_APP_ID:?GH_APP_ID não definido no .env}"
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
