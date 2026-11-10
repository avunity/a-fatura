import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, Modal, Page, PeriodPicker, Stat } from '../components/ui'
import { date, eur, lastMonth, MONTHS, pct } from '../util'

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
  const toggle = (id: string) => setSelected(s => (s.includes(id) ? s.filter(x => x !== id) : [...s, id]))
  const monthName = `${MONTHS[p.month - 1]} ${p.year}`

  return (
    <Page title="Painel do contabilista" actions={<>
      <PeriodPicker year={p.year} month={p.month} onChange={(y, m) => setP({ year: y, month: m ?? 1 })} />
      <button disabled={selected.length === 0} onClick={() => setBroadcast({ subject: `Documentos de ${monthName}`, body: `Bom dia,\n\nRelembramos que os documentos de ${monthName} devem ser carregados no A-Fatura.\n\nObrigado.` })}>✉️ Mensagem a {selected.length} cliente(s)</button>
    </>}>
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

      <Card title={`Clientes · ${monthName}`} right={<select value={sort} onChange={e => setSort(e.target.value as typeof sort)}><option value="score">Ordenar por pontuação</option><option value="missing">Por documentos em falta</option><option value="name">Por nome</option></select>}>
        <table className="table">
          <thead><tr>
            <th><input type="checkbox" checked={selected.length === rows.length && rows.length > 0} onChange={e => setSelected(e.target.checked ? rows.map(r => r.organizationId) : [])} /></th>
            <th>Cliente</th><th>Pontuação</th><th>Docs</th><th className="num">Total</th><th>e-Fatura</th><th>Cobertura</th><th>No prazo</th><th>Rever</th><th>Último envio</th><th>e-Fatura até</th><th></th>
          </tr></thead>
          <tbody>{rows.map(r => (
            <tr key={r.organizationId}>
              <td><input type="checkbox" checked={selected.includes(r.organizationId)} onChange={() => toggle(r.organizationId)} /></td>
              <td><Link to={`/o/${r.organizationId}`}>{r.name}</Link><div className="muted small">{r.nif} · {r.type === 'Individual' ? 'Particular' : 'Empresa'}</div></td>
              <td><span className={`pill ${tone(r.score)}`}>{r.score ?? '—'}</span></td>
              <td>{r.invoices}</td>
              <td className="num">{eur(r.total)}</td>
              <td>{r.eFaturaMatched}/{r.eFaturaRecords}{r.missing > 0 && <Link to={`/o/${r.organizationId}/efatura`} className="bad-text"> ({r.missing} em falta)</Link>}</td>
              <td>{pct(r.coverage)}</td>
              <td>{pct(r.onTime)}</td>
              <td>{r.needsReview > 0 ? <Link to={`/o/${r.organizationId}/invoices?status=NeedsReview&year=${p.year}&month=${p.month}`}>{r.needsReview}</Link> : 0}</td>
              <td className="small">{date(r.lastUpload)}</td>
              <td className="small">{r.eFaturaCoveredUntil ? date(r.eFaturaCoveredUntil) : <span className="warn-text">nunca importado</span>}</td>
              <td className="small">
                {r.periodClosed && '🔒 '}
                {r.unreadMessages > 0 && <Link to={`/o/${r.organizationId}/messages`}>✉️ {r.unreadMessages}</Link>}
                {r.openRequests > 0 && <span title="Pedidos de documentos em aberto"> 📄 {r.openRequests}</span>}
              </td>
            </tr>))}
          </tbody>
        </table>
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
