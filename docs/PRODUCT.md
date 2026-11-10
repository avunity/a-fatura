# A-Fatura — Documento de produto

**Objetivo:** ajudar empresas e particulares a recolher, classificar e validar faturas e despesas, e entregá-las ao contabilista completas, a tempo e já cruzadas com o e-Fatura da AT.

---

## 1. Perfis

| Perfil | Vê | Pode |
|---|---|---|
| **Gestor da empresa** | Todos os dados da empresa | Carregar e editar faturas, gerir colaboradores, viaturas, contas bancárias e categorias, importar e-Fatura e extratos, aprovar reembolsos, associar o contabilista |
| **Colaborador** | Só os documentos que carregou (passa a ver tudo se for promovido a gestor) | Carregar faturas (web, app, email próprio), corrigir os seus documentos, marcar despesas "paguei eu" |
| **Contabilista** | Todas as empresas e particulares que lhe estão atribuídos | Consultar tudo, rever, pedir documentos em falta, fechar meses, exportar o mês em ZIP, enviar mensagens individuais ou em massa, painel com ranking de clientes |
| **Pessoa individual** | Os seus dados | O mesmo que um gestor, mas sem colaboradores |
| **Backoffice (Admin)** | Toda a plataforma | Estatísticas, entidades, utilizadores, criar contabilistas, atribuir contabilistas, suspender contas, auditoria. Também abre qualquer entidade como se fosse gestor |

Modelo: um particular é uma *organização* do tipo `Individual` com um único gestor. Isto permite usar o mesmo código para os dois casos e passar mais tarde de particular para empresa sem migrar dados.

## 2. Funcionalidades da 1.ª fase (implementadas)

### Recolha de documentos
- **Upload web** (vários ficheiros de uma vez, PDF ou imagem), **app mobile KMP** (câmara ou ficheiro) e **email por colaborador**: cada membro tem um endereço próprio `token@in.<domínio>`. Reencaminha a fatura e ela fica registada em seu nome. O endereço pode ser regenerado se for exposto.
- Receção de email por **webhook** (MIME raw, compatível com Cloudflare Email Workers, Mailgun, SendGrid e Postfix) **ou** por **IMAP catch-all**.

### Extração e validação
1. **Código QR da AT** (Portaria 195/2020): lido do PDF ou da fotografia. Dá NIF do emitente e do adquirente, tipo, número, ATCUD, data, base e IVA por taxa e região (PT, Açores, Madeira) e o total. É a fonte mais fiável, porque contém os dados oficiais.
2. **IA** (Claude CLI já autenticado na VPS, ou API da Anthropic): nome do fornecedor, matrícula, litros/kWh, descrição, categoria sugerida e todos os campos fiscais quando não há QR. Se já houver QR **e** o fornecedor for conhecido, a IA não é chamada, o que poupa custo.
3. **Validações automáticas:** NIF do adquirente diferente do da empresa, fatura sem NIF, NIF do emitente inválido (dígito de controlo), ATCUD em falta, base + IVA ≠ total, duplicados (mesmo fornecedor e número, mesmo ATCUD ou mesmo ficheiro), baixa confiança. Os documentos com alertas ficam em **"Rever"**.

### Classificação
- **Categorias** predefinidas (combustível, carregamento elétrico, portagens, seguro, manutenção, informática, software, restauração…) e categorias próprias de cada entidade. Cada categoria tem palavras-chave, um indicador "associada a viaturas" e uma % de IVA dedutível indicativa (a validar pelo contabilista).
- A categoria é escolhida por esta ordem: **o que o utilizador escolheu antes para o mesmo fornecedor** (aprendizagem), depois a sugestão da IA, depois as palavras-chave.
- **Viaturas** (gestor e particular): matrícula, combustível, datas de seguro e inspeção. Uma fatura é associada a uma viatura pela matrícula impressa no documento, pela viatura habitual do fornecedor, ou quando só existe uma viatura compatível (por exemplo, um carregamento elétrico vai para a única viatura elétrica). Mostra o custo anual por viatura e por categoria, com litros e kWh.

### e-Fatura
- Importação do **CSV exportado do portal e-Fatura**. As colunas são reconhecidas por nome, o encoding é UTF-8 ou Windows-1252 e o formato "NIF - Nome" e "Nº / ATCUD" é separado automaticamente.
- Cada registo tem uma chave única (NIF + nº de documento), por isso reimportar um período nunca duplica: só atualiza o estado na AT.
- O cruzamento funciona nos dois sentidos. Ao importar, as faturas já carregadas ficam marcadas como **recolhidas**. Ao carregar uma fatura nova, é procurada nos registos já importados.
- Os registos sem documento aparecem como **"em falta"** e há um botão **"Pedir documento"**, que cria o pedido e uma mensagem na inbox do cliente. O pedido fecha sozinho quando a fatura chega.

### Contas bancárias e extratos
- Entidade "conta bancária" sem ligação ao banco. Os extratos CSV são reconhecidos pelos cabeçalhos usuais dos bancos portugueses: débito/crédito ou montante, data movimento/valor, saldo.
- Cada importação regista o período. As importações que se sobrepõem geram um aviso e os **movimentos repetidos são ignorados** (hash por movimento e ocorrência). A cobertura de períodos aparece numa linha temporal.
- **Conciliação:** sugere a fatura com o mesmo valor numa janela de datas e permite ligar ou desligar.

### Histórico e consulta
- Filtros por ano, mês, categoria, viatura, estado, origem, quem carregou e recolhida/não recolhida no e-Fatura, com pesquisa por fornecedor, NIF ou número. Totais de base, IVA e total.
- O detalhe mostra o documento, os dados fiscais editáveis, a decomposição do IVA e a rastreabilidade: quem carregou, por que canal, método de extração, ligação ao e-Fatura, duplicado de, revisão.

### Contabilista
- **Painel mensal** de todos os clientes com documentos enviados, total, cobertura do e-Fatura, envios no prazo, documentos para rever, último envio e data até onde o e-Fatura foi importado.
- **Pontuação** de 0 a 100: 50% cobertura do e-Fatura, 25% sem pendentes de revisão, 25% enviados no prazo. Inclui os **5 melhores e os 5 piores** clientes.
- Mensagem em massa aos clientes selecionados, **fecho de mês** (o cliente deixa de poder alterar esse período) e exportação do mês em **ZIP** (documentos renomeados + `resumo.csv`).
- **Inbox por cliente** com conversas: as mensagens ficam registadas e há contadores de não lidas.
- **Lembretes automáticos:** 3 dias antes do prazo de entrega de cada cliente, se o mês anterior não tiver documentos, é colocada uma mensagem na inbox desse cliente.

## 3. O que vimos na concorrência e o que incluímos

| Produto | Ideia relevante | No A-Fatura |
|---|---|---|
| **Dext Prepare** (ex-Receipt Bank) | Captura por app, email e upload; acesso direto do contabilista; métricas de utilização por cliente (*Practice Insights*) | Os 3 canais de captura, o painel do contabilista com pontuação por cliente e o pedido de documentos |
| **TOConline** (OCC) | "Robot" de arquivo e classificação; importação do e-Fatura como base de verificação; despesas de colaboradores com aprovação | Classificação com aprendizagem por fornecedor, cruzamento com o e-Fatura e reembolsos com aprovação |
| **Tickelia / Exaccta** | Despesas de deslocação dos colaboradores; digitalização com validade legal (DL 28/2019) | "Paguei eu" com estados de reembolso; arquivo do original com hash SHA-256 |
| **App e-Fatura (AT)** | Leitura do código QR; classificação em lote | Leitura do QR como fonte principal dos dados fiscais |
| **Ferramentas de extração (IA/OCR)** | Validar ATCUD e QR contra o PDF; IVA por taxa em colunas separadas | Alertas de ATCUD e NIF, IVA por taxa e região |

**Extras incluídos:** deteção de duplicados, fecho de mês, pedidos de documentos, lembretes, reembolsos, conciliação bancária, alertas de seguro e inspeção das viaturas, exportação ZIP, auditoria de ações e regeneração do email pessoal.

## 4. Próximas fases (sugestão)
1. **Integração com software de contabilidade:** exportação SAF-T de compras ou CSV no formato do TOConline, Primavera e PHC.
2. **Notificações** por email, Telegram e Mattermost para o contabilista (resumo semanal) e para os clientes (pedidos).
3. **Mapa de deslocações / quilómetros** com ajudas de custo, a partir das viaturas.
4. **Fila offline na app** (enviar quando houver rede) e modo multi-página (várias fotos = um documento).
5. **Regras de classificação editáveis** ("se fornecedor X → categoria Y e viatura Z") e classificação em lote.
6. **Planos e faturação do próprio SaaS** (por número de clientes do contabilista, como o Dext).
7. **2FA** e convites por email em vez de palavra-passe temporária.
8. Armazenamento em **S3/MinIO** e antivírus (ClamAV) nos uploads de email.

## 5. Arquitetura

```
 App KMP (Android/iOS) ─┐
 Browser (React/Vite) ──┼─► nginx (web) ─► API .NET 10 ─► PostgreSQL 17
 Email (webhook/IMAP) ──┘                     │
                                              ├─ volume /data/files (documentos)
                                              ├─ QR: PDFium + SkiaSharp + ZXing
                                              ├─ IA: Claude CLI (login da VPS) | Anthropic API
                                              └─ workers: processamento, IMAP, lembretes
```

- **Backend:** ASP.NET Core Minimal APIs organizado por *feature* (Auth, Organizations, Invoices, Extraction, Classification, EFatura, Banking, Mail, Messages, Accountant, Backoffice), EF Core com Npgsql e JWT.
- **Permissões:** todas as rotas de dados ficam em `/api/orgs/{orgId}/…` e passam pelo `AccessService`, que calcula o perfil efetivo (gestor, colaborador, contabilista atribuído ou admin) e filtra as faturas dos colaboradores.
- **Processamento assíncrono:** o upload devolve logo uma resposta e um worker processa a fila. As faturas pendentes voltam à fila quando o serviço arranca.
- **Fiscal:** a % de IVA dedutível é só indicativa e configurável. A decisão final é sempre do contabilista.
