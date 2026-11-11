import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, Modal, Page, PeriodPicker, Stat } from '../components/ui'
import { date, eur, lastMonth, MONTHS, pct } from '../util'
import { ColumnPicker, DataTable, useTableLayout, type Col } from '../components/DataTable'

type Row = {
  organizationId: string; name: string; nif?: string; type: string
  invoices: number; total: number; vat: number; needsReview: number
  eFaturaRecords: number; eFaturaMatched: number; missing: number
  coverage?: number; onTime?: number; score?: number
  lastUpload?: string; lastEFaturaImport?: string; eFaturaCoveredUntil?: string
  openRequests: number; unreadMessages: number; periodClosed: boolean
}
type Dash = { totals: { clients: number; invoices: number; missing: number; needsReview: number; closed: number; noUploads: number }; best: Row[]; worst: Row[]; clients: Row[] }

const tone = (s?: number) => (s == null ? '' : s >= 80 ? 'ok' : s >= 50 ? 'warn' : 'bad')

export default function Accountant() {
  const lm = lastMonth()
  const [p, setP] = useState(lm)
  const { data } = useLoad(() => api.get<Dash>(`/api/accountant/dashboard?year=${p.year}&month=${p.month}`), [p.year, p.month])
  const [selected, setSelected] = useState<string[]>([])
  const [broadcast, setBroadcast] = useState<{ subject: string; body: string } | null>(null)
  const [sort, setSort] = useState<'score' | 'name' | 'missing'>('score')

  const rows = [...(data?.clients ?? [])].sort((a, b) =>
    sort === 'name' ? a.name.localeCompare(b.name) : sort === 'missing' ? b.missing - a.missing : (b.score ?? -1) - (a.score ?? -1))
  const columns = useMemo<Col<Row>[]>(() => [
    { id: 'client', label: 'Cliente', sort: r => r.name, width: 240, locked: true, render: r => <><Link to={`/o/${r.organizationId}`}>{r.name}</Link><div className="muted small">{r.nif} · {r.type === 'Individual' ? 'Particular' : 'Empresa'}</div></> },
    { id: 'score', label: 'Pontuação', sort: r => r.score, width: 100, render: r => <span className={`pill ${tone(r.score)}`}>{r.score ?? '—'}</span> },
    { id: 'docs', label: 'Docs', sort: r => r.invoices, width: 70, className: 'num', render: r => r.invoices },
    { id: 'total', label: 'Total', sort: r => r.total, width: 110, className: 'num', render: r => eur(r.total) },
    { id: 'vat', label: 'IVA', sort: r => r.vat, width: 100, className: 'num', defaultVisible: false, render: r => eur(r.vat) },
    { id: 'efatura', label: 'e-Fatura', sort: r => r.missing, width: 170, render: r => <>{r.eFaturaMatched}/{r.eFaturaRecords}{r.missing > 0 && <Link to={`/o/${r.organizationId}/efatura`} className="bad-text"> ({r.missing} em falta)</Link>}</> },
    { id: 'coverage', label: 'Cobertura', sort: r => r.coverage, width: 95, render: r => pct(r.coverage) },
    { id: 'onTime', label: 'No prazo', sort: r => r.onTime, width: 90, render: r => pct(r.onTime) },
    { id: 'review', label: 'Rever', sort: r => r.needsReview, width: 75, render: r => r.needsReview > 0 ? <Link to={`/o/${r.organizationId}/invoices?status=NeedsReview&year=${p.year}&month=${p.month}`}>{r.needsReview}</Link> : 0 },
    { id: 'last', label: 'Último envio', sort: r => r.lastUpload, width: 110, className: 'small', render: r => date(r.lastUpload) },
    { id: 'efUntil', label: 'e-Fatura até', sort: r => r.eFaturaCoveredUntil, width: 120, className: 'small', render: r => r.eFaturaCoveredUntil ? date(r.eFaturaCoveredUntil) : <span className="warn-text">nunca importado</span> },
    {
      id: 'flags', label: 'Avisos', sort: r => r.unreadMessages + r.openRequests, width: 110, className: 'small',
      render: r => <>
        {r.periodClosed && '🔒 '}
        {r.unreadMessages > 0 && <Link to={`/o/${r.organizationId}/messages`}>✉️ {r.unreadMessages}</Link>}
        {r.openRequests > 0 && <span title="Pedidos de documentos em aberto"> 📄 {r.openRequests}</span>}
      </>,
    },
  ], [p.year, p.month])
  const layout = useTableLayout('accountant-clients', columns)
  const toggle = (id: string) => setSelected(s => (s.includes(id) ? s.filter(x => x !== id) : [...s, id]))
  const monthName = `${MONTHS[p.month - 1]} ${p.year}`

  return (
    <Page title="Painel do contabilista" actions={<>
      <PeriodPicker year={p.year} month={p.month} onChange={(y, m) => setP({ year: y, month: m ?? 1 })} />
      <button disabled={selected.length === 0} onClick={() => setBroadcast({ subject: `Documentos de ${monthName}`, body: `Bom dia,\n\nRelembramos que os documentos de ${monthName} devem ser carregados no A-Fatura.\n\nObrigado.` })}>✉️ Mensagem a {selected.length} cliente(s)</button>
    </>}>
      {data && data.clients.length === 0 && (
        <div className="alert warn">
          Ainda não tem clientes associados. Um cliente fica associado quando o <b>gestor da empresa</b> (ou o particular) vai a
          <b> Definições → Contabilista</b> e indica o seu email, ou quando o <b>backoffice</b> faz a associação em Entidades → associar.
        </div>
      )}
      <div className="stats">
        <Stat label="Clientes" value={data?.totals.clients ?? 0} />
        <Stat label="Documentos no mês" value={data?.totals.invoices ?? 0} />
        <Stat label="Sem envios" value={data?.totals.noUploads ?? 0} tone={(data?.totals.noUploads ?? 0) > 0 ? 'warn' : 'ok'} />
        <Stat label="Em falta (vs e-Fatura)" value={data?.totals.missing ?? 0} tone={(data?.totals.missing ?? 0) > 0 ? 'bad' : 'ok'} />
        <Stat label="Para rever" value={data?.totals.needsReview ?? 0} />
        <Stat label="Meses fechados" value={`${data?.totals.closed ?? 0}/${data?.totals.clients ?? 0}`} />
      </div>

      <div className="grid2">
        <Card title="🏆 Melhores clientes do mês">
          <ol>{data?.best.map(r => <li key={r.organizationId}><Link to={`/o/${r.organizationId}`}>{r.name}</Link> <span className={`pill ${tone(r.score)}`}>{r.score}</span></li>)}</ol>
        </Card>
        <Card title="⚠️ Clientes que precisam de atenção">
          <ol>{data?.worst.map(r => <li key={r.organizationId}><Link to={`/o/${r.organizationId}`}>{r.name}</Link> <span className={`pill ${tone(r.score)}`}>{r.score}</span> <span className="muted small">{r.missing} em falta · {r.invoices} enviados</span></li>)}</ol>
        </Card>
      </div>

      <Card title={`Clientes · ${monthName}`} right={<div className="list-toolbar"><select value={sort} onChange={e => setSort(e.target.value as typeof sort)}><option value="score">Ordenar por pontuação</option><option value="missing">Por documentos em falta</option><option value="name">Por nome</option></select><ColumnPicker layout={layout} /></div>}>
<DataTable layout={layout} rows={rows} rowKey={r => r.organizationId}
          leadingHeader={<input type="checkbox" checked={selected.length === rows.length && rows.length > 0} onChange={e => setSelected(e.target.checked ? rows.map(r => r.organizationId) : [])} />}
          leadingCell={r => <input type="checkbox" checked={selected.includes(r.organizationId)} onChange={() => toggle(r.organizationId)} />}
          empty="Sem clientes associados." />
        <p className="small muted">Pontuação = 50% cobertura do e-Fatura + 25% documentos sem pendentes de revisão + 25% enviados dentro do prazo definido pelo cliente.</p>
      </Card>

      {broadcast && (
        <Modal title={`Mensagem para ${selected.length} cliente(s)`} onClose={() => setBroadcast(null)}>
          <input value={broadcast.subject} onChange={e => setBroadcast({ ...broadcast, subject: e.target.value })} />
          <textarea rows={6} value={broadcast.body} onChange={e => setBroadcast({ ...broadcast, body: e.target.value })} />
          <button className="primary" onClick={async () => { await api.post('/api/accountant/broadcast', { organizationIds: selected, ...broadcast }); setBroadcast(null); setSelected([]) }}>Enviar</button>
        </Modal>
      )}
    </Page>
  )
}
