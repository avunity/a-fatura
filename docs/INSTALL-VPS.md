# Instalar o A-Fatura na VPS a partir do GitHub

Pré-requisitos: Docker com o plugin `docker compose`, git, openssl e curl na VPS, mais um reverse proxy (nginx ou Traefik) com TLS.

## 1. DNS
- Registo `A`: `fatura.avunity.com` → IP da VPS (ou o subdomínio que escolheres; muda `PUBLIC_HOST`).
- (Email por colaborador) `MX` de `in.fatura.avunity.com` para o fornecedor que recebe o email (Cloudflare Email Routing, Mailgun…), ou uma caixa catch-all IMAP.

## 2. Acesso ao repositório privado (deploy key só de leitura)
```bash
sudo mkdir -p /opt/a-fatura && sudo chown $USER /opt/a-fatura
ssh-keygen -t ed25519 -f ~/.ssh/a-fatura_deploy -N "" -C "vps a-fatura"
cat ~/.ssh/a-fatura_deploy.pub
```
GitHub → `avunity/a-fatura` → **Settings → Deploy keys → Add deploy key** → cola a chave pública (sem "Allow write access").

```bash
cat >> ~/.ssh/config <<'EOF'
Host github-a-fatura
  HostName github.com
  User git
  IdentityFile ~/.ssh/a-fatura_deploy
  IdentitiesOnly yes
EOF
git clone github-a-fatura:avunity/a-fatura.git /opt/a-fatura
cd /opt/a-fatura
```
(Alternativa: `gh auth login` e depois `gh repo clone avunity/a-fatura /opt/a-fatura`.)

## 3. Configuração
```bash
./deploy/install.sh      # 1.ª vez: cria .env com DB_PASSWORD, JWT_KEY, MAIL_WEBHOOK_SECRET gerados e APP_UID/CLAUDE_HOME do teu utilizador
nano .env                # PUBLIC_HOST, ADMIN_EMAIL, ADMIN_PASSWORD, MAIL_INBOUND_DOMAIN, IMAP_* se aplicável
```
IA:
- `AI_PROVIDER=ClaudeCli` (por omissão) usa o login do Claude CLI que já tens no host. O `install.sh` deteta `~/.claude` e `~/.claude.json` do utilizador que o corre. Se o login foi feito como root, corre o script com o root (ou com `sudo`, que usa o `SUDO_USER`; nesse caso ajusta `APP_UID=0` e os caminhos para `/root/...`).
- Sem CLI: `AI_PROVIDER=Anthropic` + `ANTHROPIC_API_KEY`, ou `AI_PROVIDER=None` (só QR) + `INSTALL_CLAUDE_CLI=false`.

## 4. Arrancar
```bash
./deploy/install.sh      # 2.ª vez: build + up + espera pelo /api/health
docker compose ps
docker compose logs -f api   # deve mostrar "Seeded default categories" e "Seeded admin user"
```
A stack escuta apenas em `127.0.0.1:8095` (`WEB_PORT`).

## 5. Reverse proxy + HTTPS
**nginx no host**
```bash
sudo cp deploy/nginx-host.conf /etc/nginx/sites-available/fatura.avunity.com
sudo ln -s /etc/nginx/sites-available/fatura.avunity.com /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d fatura.avunity.com
```
**Traefik** (rede externa `proxy`, certresolver `letsencrypt`; ajusta os nomes em `deploy/docker-compose.traefik.yml`)
```bash
docker compose -f docker-compose.yml -f deploy/docker-compose.traefik.yml up -d --build
```

## 6. Primeiro acesso
1. `https://fatura.avunity.com` → entra com `ADMIN_EMAIL`/`ADMIN_PASSWORD` (backoffice).
2. Backoffice → Utilizadores → **Criar contabilista**.
3. Os clientes registam-se em `/register` e associam o contabilista em Definições (ou o admin associa no backoffice).
4. Quando já não precisares do registo público, define `ALLOW_SELF_REGISTRATION=false` e corre `docker compose up -d`.

## 7. Email por colaborador (opcional)
Cada colaborador tem `token@in.fatura.avunity.com`. Há três formas de receber esses emails:

### A) Recetor SMTP próprio — recomendado com o DNS na amen.pt
1. **DNS na amen.pt** (zona `avunity.com` → Gerir DNS → adicionar registo):
   | Tipo | Nome / Host | Valor | Prioridade | TTL |
   |---|---|---|---|---|
   | MX | `in.fatura` | `fatura.avunity.com.` | 10 | 3600 |
   O `fatura.avunity.com` já tem o registo `A` para a VPS. O MX tem de apontar para um nome com registo A, nunca para um IP nem para um CNAME. Se o painel pedir o nome completo, usa `in.fatura.avunity.com`.
2. **Firewall da VPS:** abre a porta 25 de entrada (`sudo ufw allow 25/tcp`, e também no firewall do fornecedor da VPS, se tiver). Confirma que nada está já a usar a porta 25: `sudo ss -ltnp | grep ':25 '`.
3. **`.env`:** `MAIL_INBOUND_DOMAIN=in.fatura.avunity.com`, `SMTP_ENABLED=true` e descomenta `COMPOSE_FILE=docker-compose.yml:deploy/docker-compose.smtp.yml`.
4. `docker compose up -d --build`. Nos logs deve aparecer `SMTP receiver listening on port 2525`.
5. **Testar:** `dig MX in.fatura.avunity.com +short` → `10 fatura.avunity.com.`. Depois envia um email com um PDF para o endereço de um colaborador e segue os logs com `docker compose logs -f api | grep SMTP`.

O recetor só aceita mensagens para endereços de colaboradores ativos e recusa tudo o resto com 550, por isso não funciona como relay nem guarda spam.

### B) Webhook de um fornecedor (Cloudflare Email Worker, Mailgun…)
POST do MIME raw para `https://fatura.avunity.com/api/inbound/email?key=<MAIL_WEBHOOK_SECRET>`.

### C) IMAP catch-all
Uma caixa que recebe `*@in.fatura.avunity.com` → `IMAP_ENABLED=true` + `IMAP_HOST/USER/PASSWORD` → `docker compose up -d`.

## 8. Backups e atualizações
```bash
sudo crontab -e
30 3 * * * /opt/a-fatura/deploy/backup.sh >> /var/log/a-fatura-backup.log 2>&1
```
Atualizar para a última versão do `main` (corre o backup antes):
```bash
cd /opt/a-fatura && ./deploy/update.sh
```

## 9. Diagnóstico
| Sintoma | Ver |
|---|---|
| API não arranca | `docker compose logs api` (falta de `JWT_KEY` com 32+ caracteres, ligação à BD) |
| Faturas ficam em "Erro" ou sem dados da IA | `docker compose exec api claude -p "ok"` (login do CLI visível no container? `APP_UID` certo?) |
| Upload 413 | `client_max_body_size` no nginx do host |
| Containers no monitor | `afatura-api-1`, `afatura-db-1`, `afatura-web-1` |
