import { useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, type Invoice } from '../api'
import { useLoad } from '../session'
import { Card, Dropzone, ErrorBox, Modal, Page, PeriodPicker, Stat, useOrgContext } from '../components/ui'
import { date, dateTime, eur, lastMonth, pct } from '../util'
import { ColumnPicker, DataTable, useTableLayout, type Col } from '../components/DataTable'

type Import = { id: string; fileName: string; periodStart: string; periodEnd: string; recordsTotal: number; recordsNew: number; recordsAlreadyKnown: number; recordsMatched: number; createdAt: string }
type Rec = {
  id: string; supplierNif: string; supplierName?: string; documentNumber: string; atcud?: string; documentType?: string; issueDate: string
  total: number; vat?: number; atStatus?: string; matchedInvoiceId?: string; matchedInvoiceLabel?: string; matchedInvoiceMulti?: boolean
  ignored: boolean; comment?: string; updatedAt?: string; requested: boolean
}
type State = 'all' | 'missing' | 'matched' | 'ignored'

const stateOf = (r: Rec): State => (r.matchedInvoiceId ? 'matched' : r.ignored ? 'ignored' : 'missing')

export default function EFatura() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  // "Pedir documento" goes from the accountant to the client's inbox: only when there is a client account to receive it.
  const canRequest = !!org && (org.access.isAccountant || org.access.isAdmin) && org.activeMembers > 0
  const lm = lastMonth()
  const [period, setPeriod] = useState<{ year: number; month?: number }>(lm)
  const [state, setState] = useState<State>('all')
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  useEffect(() => { const t = setTimeout(() => setQ(search.trim()), 350); return () => clearTimeout(t) }, [search])

  const { data: imports, reload: reloadImports } = useLoad(() => api.get<Import[]>(`/api/orgs/${orgId}/efatura/imports`), [orgId])
  const { data: records, reload } = useLoad(
    () => api.get<Rec[]>(`/api/orgs/${orgId}/efatura/records?year=${period.year}${period.month ? `&month=${period.month}` : ''}${q ? `&q=${encodeURIComponent(q)}` : ''}`),
    [orgId, period.year, period.month, q])
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [note, setNote] = useState<{ rec: Rec; comment: string; ignored: boolean } | null>(null)
  const [linking, setLinking] = useState<Rec | null>(null)

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

  const run = async (fn: () => Promise<unknown>) => { setErr(null); try { await fn(); await reload() } catch (e) { setErr((e as Error).message) } }

  const columns = useMemo<Col<Rec>[]>(() => [
    { id: 'date', label: 'Data', sort: r => r.issueDate, width: 100, render: r => date(r.issueDate) },
    {
      id: 'supplier', label: 'Emitente', sort: r => r.supplierName ?? r.supplierNif, width: 260, locked: true,
      render: r => <>{r.supplierName}<div className="muted small">{r.supplierNif}</div>{r.comment && <div className="comment">💬 {r.comment}</div>}</>,
    },
    { id: 'document', label: 'Documento', sort: r => r.documentNumber, width: 170, className: 'small', render: r => <>{r.documentNumber}<div className="muted">{r.atcud}</div></> },
    { id: 'type', label: 'Tipo', sort: r => r.documentType, width: 110, className: 'small', defaultVisible: false, render: r => r.documentType ?? '' },
    { id: 'vat', label: 'IVA', sort: r => r.vat, width: 90, className: 'num', render: r => eur(r.vat) },
    { id: 'total', label: 'Total', sort: r => r.total, width: 100, className: 'num', render: r => eur(r.total) },
    { id: 'atStatus', label: 'Situação AT', sort: r => r.atStatus, width: 110, className: 'small', render: r => r.atStatus ?? '' },
    {
      id: 'doc', label: 'Documento carregado', sort: r => stateOf(r), width: 230, className: 'small',
      render: r => r.matchedInvoiceId ? (
        <>
          <a href={`/o/${orgId}/invoices/${r.matchedInvoiceId}`} className="ok">✓ {r.matchedInvoiceLabel ?? 'carregado'}</a>
          {r.matchedInvoiceMulti && <span className="pill" title="Este documento contém várias faturas">várias faturas</span>}
        </>
      ) : r.ignored ? <span className="pill">Ignorada</span>
        : r.requested ? <span className="muted">pedido enviado</span>
        : canRequest ? <button onClick={() => run(() => api.post(`/api/orgs/${orgId}/efatura/records/${r.id}/request`))} title="Cria um pedido para o cliente carregar este documento e envia-lhe uma mensagem na inbox">Pedir documento</button>
        : <span className="muted">em falta</span>,
    },
    {
      id: 'actions', label: 'Ações', width: 190, className: 'row-actions',
      render: r => <>
        <button className="link" onClick={() => setNote({ rec: r, comment: r.comment ?? '', ignored: r.ignored })}>{r.comment || r.ignored ? 'Editar nota' : 'Comentar / ignorar'}</button>
        {r.matchedInvoiceId
          ? <button className="link" onClick={() => confirm('Desassociar este documento desta linha?') && run(() => api.post(`/api/orgs/${orgId}/efatura/records/${r.id}/link`, { invoiceId: null }))}>Desassociar</button>
          : <button className="link" onClick={() => setLinking(r)}>Associar documento</button>}
      </>,
    },
    { id: 'updated', label: 'Atualizado', sort: r => r.updatedAt, width: 130, className: 'small muted', defaultVisible: false, render: r => dateTime(r.updatedAt) },
  ], [orgId, canRequest]) // eslint-disable-line react-hooks/exhaustive-deps
  const layout = useTableLayout('efatura', columns)

  const all = records ?? []
  const counts = { all: all.length, missing: all.filter(r => stateOf(r) === 'missing').length, matched: all.filter(r => stateOf(r) === 'matched').length, ignored: all.filter(r => r.ignored && !r.matchedInvoiceId).length }
  const shown = state === 'all' ? all : all.filter(r => stateOf(r) === state)
  const relevant = counts.all - counts.ignored

  return (
    <Page title="e-Fatura (AT)" actions={<PeriodPicker year={period.year} month={period.month} allowAllMonths onChange={(y, m) => setPeriod({ year: y, month: m })} />}>
      <div className="grid2">
        <Card title="Importar CSV do e-Fatura">
          <p className="small muted">Portal e-Fatura → Faturas → Consultar faturas (adquirente) → escolher o período → Exportar CSV. Os registos repetidos de importações anteriores são atualizados, nunca duplicados.</p>
          <Dropzone label="Ficheiro CSV do e-Fatura" accept=".csv,text/csv" multiple={false} onFiles={upload} />
          {msg && <div className="alert ok">{msg}</div>}
        </Card>
        <Card title="Importações">
          <table className="table small">
            <thead><tr><th>Período</th><th>Registos</th><th>Novos</th><th>Ligados</th><th>Data</th></tr></thead>
            <tbody>{imports?.map(i => <tr key={i.id}><td>{date(i.periodStart)} – {date(i.periodEnd)}</td><td>{i.recordsTotal}</td><td>{i.recordsNew}</td><td>{i.recordsMatched}</td><td>{dateTime(i.createdAt)}</td></tr>)}</tbody>
          </table>
        </Card>
      </div>

      <div className="stats">
        <Stat label="Faturas na AT" value={counts.all} hint={counts.ignored ? `${counts.ignored} ignoradas` : undefined} />
        <Stat label="Com documento carregado" value={counts.matched} tone="ok" />
        <Stat label="Em falta" value={counts.missing} tone={counts.missing > 0 ? 'warn' : 'ok'} />
        <Stat label="Cobertura" value={pct(relevant ? counts.matched / relevant : null)} hint="sem contar as ignoradas" />
      </div>

      <ErrorBox error={err} />
      <Card>
        <div className="list-toolbar">
          <input className="search" placeholder="Pesquisar emitente, NIF, nº, ATCUD, comentário ou valor (ex: 12,30)…" value={search} onChange={e => setSearch(e.target.value)} />
          <div className="tabs inline">
            {([['all', 'Todas'], ['missing', 'Em falta'], ['matched', 'Com documento'], ['ignored', 'Ignoradas']] as const).map(([k, l]) => (
              <button key={k} className={state === k ? 'on' : ''} onClick={() => setState(k)}>{l} <span className="muted small">{counts[k]}</span></button>
            ))}
          </div>
          <ColumnPicker layout={layout} />
        </div>
        <div className="ef-legend small">
          <span><i className="ef-swatch ef-matched" />Com documento carregado</span>
          <span><i className="ef-swatch ef-missing" />Em falta</span>
          <span><i className="ef-swatch ef-ignored" />Ignorada</span>
        </div>
        <DataTable layout={layout} rows={shown} rowKey={r => r.id} rowClassName={r => `ef-row ef-${stateOf(r)}`} className="ef-table"
          empty={`Sem registos${q ? ` para "${q}"` : ''}.`} />
      </Card>

      {note && (
        <Modal title={`${note.rec.supplierName ?? note.rec.supplierNif} · ${note.rec.documentNumber}`} onClose={() => setNote(null)}>
          <p className="small muted">{date(note.rec.issueDate)} · {eur(note.rec.total)}</p>
          <label>Comentário<textarea rows={3} value={note.comment} onChange={e => setNote({ ...note, comment: e.target.value })} placeholder="ex: despesa pessoal com o NIF da empresa; já incluída no documento X…" /></label>
          <label className="inline"><input type="checkbox" checked={note.ignored} onChange={e => setNote({ ...note, ignored: e.target.checked })} /> Ignorar esta linha (não precisa de documento — deixa de contar como "em falta")</label>
          <div className="row gap">
            <button className="primary" onClick={async () => { await run(() => api.put(`/api/orgs/${orgId}/efatura/records/${note.rec.id}`, { comment: note.comment, ignored: note.ignored })); setNote(null) }}>Guardar</button>
            <button onClick={() => setNote(null)}>Cancelar</button>
          </div>
        </Modal>
      )}

      {linking && <LinkModal orgId={orgId!} rec={linking} onClose={() => setLinking(null)} onLinked={async () => { setLinking(null); await reload() }} />}
    </Page>
  )
}

/** Pick an uploaded document for an e-Fatura line (useful when one file holds several invoices, e.g. Via Verde). */
function LinkModal({ orgId, rec, onClose, onLinked }: { orgId: string; rec: Rec; onClose: () => void; onLinked: () => void }) {
  const [term, setTerm] = useState(rec.supplierName?.split(' ')[0] ?? rec.supplierNif)
  const [q, setQ] = useState(term)
  useEffect(() => { const t = setTimeout(() => setQ(term.trim()), 350); return () => clearTimeout(t) }, [term])
  const year = new Date(rec.issueDate).getFullYear()
  const { data } = useLoad(() => api.get<{ items: Invoice[] }>(`/api/orgs/${orgId}/invoices?year=${year}&pageSize=50${q ? `&q=${encodeURIComponent(q)}` : ''}`), [orgId, q, year])
  const [err, setErr] = useState<string | null>(null)

  return (
    <Modal title="Associar a um documento carregado" onClose={onClose}>
      <p className="small muted">{rec.supplierName} · {rec.documentNumber} · {date(rec.issueDate)} · <b>{eur(rec.total)}</b></p>
      <input placeholder="Pesquisar documentos…" value={term} onChange={e => setTerm(e.target.value)} autoFocus />
      <ErrorBox error={err} />
      <div className="link-list">
        {data?.items.map(i => (
          <button key={i.id} className="popover-item" onClick={async () => {
            try { await api.post(`/api/orgs/${orgId}/efatura/records/${rec.id}/link`, { invoiceId: i.id }); onLinked() } catch (e) { setErr((e as Error).message) }
          }}>
            <span className="org-text">
              <span className="org-name">{i.supplierName ?? i.fileName ?? 'Documento'} {i.subDocuments.length > 1 && <span className="pill">{i.subDocuments.length} faturas</span>}</span>
              <span className="org-tag">{date(i.issueDate)} · {i.documentNumber ?? '—'} · {eur(i.totalAmount)}{i.collectedInEFatura ? ' · já ligado a outra linha' : ''}</span>
            </span>
          </button>
        ))}
        {data?.items.length === 0 && <p className="muted small">Nenhum documento encontrado.</p>}
      </div>
    </Modal>
  )
}
