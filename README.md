# A-Fatura

Gestão de faturas e despesas para empresas, particulares e contabilistas: upload, app e email → leitura do QR da AT + IA → classificação (categorias e viaturas) → cruzamento com o e-Fatura e os extratos → painel do contabilista.

Especificação funcional e comparação com a concorrência: [`docs/PRODUCT.md`](docs/PRODUCT.md)

```
backend/   API .NET 10 (AFatura.Api) + testes (AFatura.Tests)
frontend/  React 19 + Vite (SPA servida por nginx, proxy /api → api)
mobile/    App Kotlin Multiplatform (Compose) — Android + iOS
deploy/    vhost nginx do host, override Traefik, backup
```

## Deploy na VPS (novo subdomínio, Docker)

> Guia completo a partir do GitHub: [`docs/INSTALL-VPS.md`](docs/INSTALL-VPS.md) — resumo: clonar → `./deploy/install.sh` (gera `.env`) → editar `.env` → `./deploy/install.sh` → reverse proxy. Atualizações: `./deploy/update.sh`.

1. **DNS:** registo `A` de `fatura.avunity.com` para o IP da VPS. Para o email por colaborador, também `MX` de `in.fatura.avunity.com` (ou o encaminhamento do teu fornecedor de email; ver abaixo).
2. **Código:**
   ```bash
   git clone git@github.com:avunity/a-fatura.git /opt/a-fatura && cd /opt/a-fatura
   cp .env.example .env
   openssl rand -base64 48   # → JWT_KEY
   openssl rand -base64 24   # → DB_PASSWORD, MAIL_WEBHOOK_SECRET
   nano .env                 # PUBLIC_HOST, ADMIN_EMAIL/ADMIN_PASSWORD, etc.
   ```
3. **Claude CLI partilhado:** o container usa o login do Claude CLI que já tens no host.
   - `CLAUDE_HOME` / `CLAUDE_JSON` apontam para o `~/.claude` e o `~/.claude.json` do utilizador que fez login (ex.: `/root/.claude`).
   - `APP_UID` tem de ser o uid desse utilizador (`id -u`; `0` se for root), para o container conseguir ler as credenciais.
   - Confirma que `~/.claude.json` existe antes do primeiro `up`. Se não existir, o Docker cria uma pasta com esse nome.
   - Alternativa: `AI_PROVIDER=Anthropic` + `ANTHROPIC_API_KEY`, ou `AI_PROVIDER=None` (só QR) + `INSTALL_CLAUDE_CLI=false`.
4. **Arrancar:**
   ```bash
   docker compose up -d --build
   docker compose logs -f api      # "Seeded default categories", "Seeded admin user"
   curl -s http://127.0.0.1:8095/api/health
   ```
5. **Reverse proxy + TLS:**
   - **nginx no host:** `deploy/nginx-host.conf` → `certbot --nginx -d fatura.avunity.com`
   - **Traefik:** `docker compose -f docker-compose.yml -f deploy/docker-compose.traefik.yml up -d --build` (rede externa `proxy`, certresolver `letsencrypt`; ajusta aos nomes que usas)
6. **Backups:** `deploy/backup.sh` no cron (dump do Postgres + volume dos documentos, retenção de 14 dias). Faz backup antes de cada atualização.
7. **Monitorização:** o container `api` tem healthcheck em `/api/health`. Como o projeto se chama `afatura`, os containers ficam como `afatura-api-1`, `afatura-db-1` e `afatura-web-1`, fáceis de adicionar ao monitor.avunity.com.

### Primeiros passos na aplicação
1. Entra com `ADMIN_EMAIL` → **Backoffice → Utilizadores → Criar contabilista** (recebes uma palavra-passe temporária).
2. Os clientes registam-se em `/register` (empresa ou particular). O gestor associa o contabilista em **Definições** (pelo email), ou o admin faz a associação no backoffice.
3. Para desligar o registo público, usa `ALLOW_SELF_REGISTRATION=false`. Nesse caso, as contas passam a ser criadas pelo gestor (colaboradores) ou pelo backoffice.

### Email por colaborador
Cada membro tem `token@MAIL_INBOUND_DOMAIN` (visível em **Colaboradores** e na app). Há duas formas de receber:
- **Webhook (recomendado):** o fornecedor entrega o MIME raw em `POST https://fatura.avunity.com/api/inbound/email?key=MAIL_WEBHOOK_SECRET`.
  Funciona com Cloudflare Email Routing + Email Worker (`fetch(url, {method:'POST', body: message.raw})`), Mailgun Routes (store/forward com `body-mime`), SendGrid Inbound Parse ("POST raw") ou um alias Postfix com `| curl --data-binary @- …`.
- **IMAP catch-all:** uma caixa que recebe `*@in.fatura.avunity.com`. Configura `IMAP_*` com `IMAP_ENABLED=true`. As mensagens processadas passam para a pasta `Processed`.

## Desenvolvimento local
```bash
docker run -d --name afatura-pg -e POSTGRES_USER=afatura -e POSTGRES_PASSWORD=afatura -e POSTGRES_DB=afatura -p 5432:5432 postgres:17-alpine
cd backend/AFatura.Api && Storage__Root=./data Admin__Email=admin@local Admin__Password=admin12345 dotnet run --urls http://localhost:8080
cd frontend && npm install && npm run dev        # http://localhost:5173 (proxy /api → :8080)
cd backend && dotnet test                        # parsers: QR AT, e-Fatura CSV, extratos, NIF, montantes
```
**Migrações:** o primeiro arranque cria o esquema com `EnsureCreated`. Antes de alterar entidades em produção, cria uma migração inicial (`dotnet ef migrations add Initial`). A partir daí, o arranque aplica `Migrate()` automaticamente.

## Pipeline de uma fatura
1. Ficheiro guardado em `/data/files/{org}/{yyyy}/{MM}/…` com SHA-256; fatura em estado `Pending` na fila.
2. **QR da AT** (PDF via PDFium ou foto via ZXing): dados fiscais oficiais.
3. **IA** (só quando necessária): fornecedor, matrícula, litros/kWh, categoria e campos em falta.
4. Duplicados → categoria (fornecedor aprendido > IA > palavras-chave) → viatura → **e-Fatura** → validações.
5. Fica `Processed`, ou `NeedsReview` se houver alertas. As correções do utilizador ficam memorizadas para o fornecedor.

## API (resumo)
| Área | Rotas |
|---|---|
| Auth | `POST /api/auth/register · login · change-password`, `GET /api/auth/me` |
| Entidade | `GET/PUT /api/orgs/{id}`, `…/members`, `…/accountant`, `…/audit` |
| Faturas | `GET/POST /api/orgs/{id}/invoices`, `…/{inv}` `…/{inv}/file` `…/review` `…/reprocess` `…/reimbursement`, `…/summary`, `…/export` |
| Mestres | `…/vehicles`, `…/categories` |
| e-Fatura | `…/efatura/imports`, `…/efatura/records`, `…/records/{r}/request` |
| Bancos | `…/bank-accounts`, `…/{acc}/statements`, `…/transactions`, `…/suggestions`, `…/match` |
| Mensagens | `…/messages`, `…/messages/read`, `GET /api/inbox` |
| Contabilista | `GET /api/accountant/dashboard`, `POST /api/accountant/broadcast`, `…/periods/{y}/{m}/close`, `…/requests` |
| Backoffice | `/api/backoffice/stats · organizations · users · assignments · audit` |
| Email | `POST /api/inbound/email?key=…` |
