import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, Dropzone, ErrorBox, Page, PeriodPicker, Stat } from '../components/ui'
import { date, dateTime, eur, lastMonth, pct } from '../util'

type Import = { id: string; fileName: string; periodStart: string; periodEnd: string; recordsTotal: number; recordsNew: number; recordsAlreadyKnown: number; recordsMatched: number; createdAt: string }
type Rec = { id: string; supplierNif: string; supplierName?: string; documentNumber: string; atcud?: string; documentType?: string; issueDate: string; total: number; vat?: number; atStatus?: string; matchedInvoiceId?: string; requested: boolean }

export default function EFatura() {
  const { orgId } = useParams()
  const lm = lastMonth()
  const [period, setPeriod] = useState<{ year: number; month?: number }>(lm)
  const [onlyMissing, setOnlyMissing] = useState(false)
  const { data: imports, reload: reloadImports } = useLoad(() => api.get<Import[]>(`/api/orgs/${orgId}/efatura/imports`), [orgId])
  const { data: records, reload } = useLoad(
    () => api.get<Rec[]>(`/api/orgs/${orgId}/efatura/records?year=${period.year}${period.month ? `&month=${period.month}` : ''}${onlyMissing ? '&matched=false' : ''}`),
    [orgId, period.year, period.month, onlyMissing])
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  async function upload(files: File[]) {
    if (!files[0]) return
    setErr(null); setMsg(null)
    const form = new FormData()
    form.append('file', files[0])
    try {
      const r = await api.upload<{ import: Import; missing: number }>(`/api/orgs/${orgId}/efatura/imports`, form)
      setMsg(`${r.import.recordsTotal} registos lidos (${r.import.recordsNew} novos, ${r.import.recordsAlreadyKnown} já conhecidos). ${r.import.recordsMatched} faturas marcadas como recolhidas. ${r.missing} sem documento carregado.`)
      reloadImports(); reload()
    } catch (e) { setErr((e as Error).message) }
  }

  const total = records?.length ?? 0
  const matched = records?.filter(r => r.matchedInvoiceId).length ?? 0

  return (
    <Page title="e-Fatura (AT)" actions={<PeriodPicker year={period.year} month={period.month} allowAllMonths onChange={(y, m) => setPeriod({ year: y, month: m })} />}>
      <div className="grid2">
        <Card title="Importar CSV do e-Fatura">
          <p className="small muted">Portal e-Fatura → Faturas → Consultar faturas (adquirente) → escolher o período → Exportar CSV. Os registos repetidos de importações anteriores são atualizados, nunca duplicados.</p>
          <Dropzone label="Ficheiro CSV do e-Fatura" accept=".csv,text/csv" multiple={false} onFiles={upload} />
          <ErrorBox error={err} />
          {msg && <div className="alert ok">{msg}</div>}
        </Card>
        <Card title="Importações">
          <table className="table small">
            <thead><tr><th>Período</th><th>Registos</th><th>Novos</th><th>Ligados</th><th>Data</th></tr></thead>
            <tbody>{imports?.map(i => <tr key={i.id}><td>{date(i.periodStart)} – {date(i.periodEnd)}</td><td>{i.recordsTotal}</td><td>{i.recordsNew}</td><td>{i.recordsMatched}</td><td>{dateTime(i.createdAt)}</td></tr>)}</tbody>
          </table>
        </Card>
      </div>

      {!onlyMissing && (
        <div className="stats">
          <Stat label="Faturas na AT" value={total} />
          <Stat label="Com documento carregado" value={matched} tone="ok" />
          <Stat label="Em falta" value={total - matched} tone={total - matched > 0 ? 'warn' : 'ok'} />
          <Stat label="Cobertura" value={pct(total ? matched / total : null)} />
        </div>
      )}

      <Card title="Registos" right={<label className="inline small"><input type="checkbox" checked={onlyMissing} onChange={e => setOnlyMissing(e.target.checked)} /> Só os que não têm documento</label>}>
        <table className="table">
          <thead><tr><th>Data</th><th>Emitente</th><th>Documento</th><th className="num">IVA</th><th className="num">Total</th><th>Situação AT</th><th>Documento</th></tr></thead>
          <tbody>{records?.map(r => (
            <tr key={r.id}>
              <td>{date(r.issueDate)}</td>
              <td>{r.supplierName}<div className="muted small">{r.supplierNif}</div></td>
              <td className="small">{r.documentNumber}<div className="muted">{r.atcud}</div></td>
              <td className="num">{eur(r.vat)}</td><td className="num">{eur(r.total)}</td>
              <td className="small">{r.atStatus}</td>
              <td>{r.matchedInvoiceId
                ? <a href={`/o/${orgId}/invoices/${r.matchedInvoiceId}`} className="ok">✓ carregado</a>
                : r.requested ? <span className="muted">pedido enviado</span>
                : <button onClick={async () => { await api.post(`/api/orgs/${orgId}/efatura/records/${r.id}/request`); reload() }}>Pedir documento</button>}
              </td>
            </tr>))}
          </tbody>
        </table>
      </Card>
    </Page>
  )
}
