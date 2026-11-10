# CI/CD — GitHub Actions

O workflow `.github/workflows/build.yml` corre em cada push para o `main`, em cada pull request e em cada tag `v*.*.*`.

| Job | O que faz | Resultado |
|---|---|---|
| **backend** | `dotnet test` (compila a API e corre os testes dos parsers) | ✓/✗ |
| **android** | `gradle :composeApp:assembleRelease` (com keystore) ou `assembleDebug` (sem keystore) | artefacto `android-apk` |
| **ios** | compila o framework iOS no macOS (verificação; não gera `.ipa`) | ✓/✗ (não bloqueia) |
| **images** | build + push para o GHCR: `ghcr.io/avunity/a-fatura-api` e `-web`. A imagem web já leva o APK em `/downloads/a-fatura.apk` | `:latest`, `:<sha>`, `:<versão>` |
| **web-bundle** | `npm run build` → `a-fatura-web.zip` (SPA estática) | artefacto `web-bundle` |
| **release** | só em tags: cria a GitHub Release com `a-fatura.apk` e `a-fatura-web.zip` | Release |

Os artefactos ficam em **Actions → execução → Artifacts** durante 30 dias. Os clientes descarregam o APK diretamente do site: `https://fatura.avunity.com/downloads/a-fatura.apk`. Também há um link na página de login e no menu.

## Publicar uma versão
```bash
git tag v1.0.0 && git push origin v1.0.0
```
Na VPS, usa `IMAGE_TAG=1.0.0` no `.env` para fixar a versão (ou `latest` para seguir o `main`) e depois corre `./deploy/update.sh`.

## VPS: acesso às imagens (pacotes privados)
**Com o gh CLI já autenticado na VPS (mais simples):** o `deploy/ghcr-login.sh` usa o `gh auth token`. Basta acrescentar uma vez o scope dos pacotes: `gh auth refresh -h github.com -s read:packages`.

**Com a GitHub App (recomendado):** a app precisa da permissão *Packages: Read*. No `.env`, preenche `GH_APP_ID` e `GH_APP_KEY` (caminho do `.pem`). O `GH_APP_INSTALLATION_ID` é opcional. O `update.sh` corre `deploy/ghcr-login.sh` antes de cada `pull`, porque os tokens da app duram 1 hora. Para testar: `./deploy/ghcr-login.sh && docker compose pull`.

**Alternativa com um PAT:**
O repositório é privado, por isso as imagens no GHCR também são. Faz login uma vez na VPS:
1. GitHub → Settings → Developer settings → **Personal access tokens (classic)** → scope **`read:packages`** apenas.
2. Na VPS:
   ```bash
   echo <TOKEN> | docker login ghcr.io -u <utilizador-github> --password-stdin
   ```
A partir daqui, o `./deploy/update.sh` faz `git pull` (para ter o compose e os scripts novos), backup, `docker compose pull` e `up`, sem compilar nada. Para compilar na VPS como antes, usa `BUILD_LOCAL=true ./deploy/update.sh`.

## Android: assinatura de release (recomendado)
Sem keystore, o CI gera um APK **debug**. Instala-se igual, mas cada build pode vir assinado com uma chave diferente e, nesse caso, o Android obriga a desinstalar antes de atualizar. Com um keystore fixo, as atualizações instalam-se por cima.
```bash
keytool -genkeypair -v -keystore a-fatura-release.jks -alias afatura -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 a-fatura-release.jks > keystore.b64
```
GitHub → repo → **Settings → Secrets and variables → Actions → New repository secret**:
| Secret | Valor |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | conteúdo de `keystore.b64` |
| `ANDROID_KEYSTORE_PASSWORD` | palavra-passe do keystore |
| `ANDROID_KEY_ALIAS` | `afatura` |
| `ANDROID_KEY_PASSWORD` | palavra-passe da chave |

Guarda o `.jks` num sítio seguro: se o perderes, deixas de conseguir publicar atualizações compatíveis.

## iOS: o que falta para ter uma app instalável
A Apple não permite instalar `.ipa` por download direto como no Android. É preciso:
1. **Conta Apple Developer** (99 €/ano).
2. O projeto Xcode **`mobile/iosApp`**, gerado com o wizard KMP e ligado ao framework `ComposeApp`.
3. Certificado de distribuição + provisioning profile como secrets, e um job `xcodebuild archive` + upload para o **TestFlight**. Os utilizadores instalam pela app TestFlight, ou mais tarde pela App Store.

Até lá, o job `ios` garante que o código partilhado e o código iOS compilam.
