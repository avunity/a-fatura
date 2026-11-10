import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, type Category, type Invoice, type Vehicle } from '../api'
import { useLoad } from '../session'
import { Card, Dropzone, ErrorBox, Page, PeriodPicker, StatusPill, useOrgContext } from '../components/ui'
import { date, eur, SOURCE_LABEL } from '../util'

type List = { total: number; sums?: { total: number; vat: number; net: number }; items: Invoice[] }

export default function Invoices() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const [params, setParams] = useSearchParams()
  const now = new Date()
  const year = Number(params.get('year') ?? now.getFullYear())
  const month = params.get('month') ? Number(params.get('month')) : undefined
  const filter = (k: string, v?: string | number) => {
    const p = new URLSearchParams(params)
    if (v === undefined || v === '') p.delete(k); else p.set(k, String(v))
    setParams(p, { replace: true })
  }

  const qs = new URLSearchParams(params)
  if (!qs.has('year')) qs.set('year', String(year))
  const { data, error, reload } = useLoad(() => api.get<List>(`/api/orgs/${orgId}/invoices?${qs}&pageSize=200`), [orgId, params.toString()])
  const { data: categories } = useLoad(() => api.get<Category[]>(`/api/orgs/${orgId}/categories`), [orgId])
  const { data: vehicles } = useLoad(() => api.get<Vehicle[]>(`/api/orgs/${orgId}/vehicles`), [orgId])

  const [uploadVehicle, setUploadVehicle] = useState('')
  const [paidPersonally, setPaidPersonally] = useState(false)
  const [uploading, setUploading] = useState<string | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)

  // Poll while documents are being processed
  const busy = data?.items.some(i => i.status === 'Pending' || i.status === 'Processing')
  useEffect(() => {
    if (!busy) return
    const t = setInterval(reload, 4000)
    return () => clearInterval(t)
  }, [busy, reload])

  async function upload(files: File[]) {
    if (files.length === 0) return
    setUploadError(null)
    const form = new FormData()
    files.forEach(f => form.append('files', f))
    if (uploadVehicle) form.append('vehicleId', uploadVehicle)
    if (paidPersonally) form.append('paidPersonally', 'true')
    setUploading(`A enviar ${files.length} ficheiro(s)…`)
    try { await api.upload(`/api/orgs/${orgId}/invoices`, form); await reload() }
    catch (e) { setUploadError((e as Error).message) }
    finally { setUploading(null) }
  }

  const canExport = org?.access.canSeeAll && month

  return (
    <Page title="Faturas" actions={<>
      <PeriodPicker year={year} month={month} allowAllMonths onChange={(y, m) => { const p = new URLSearchParams(params); p.set('year', String(y)); if (m) p.set('month', String(m)); else p.delete('month'); setParams(p, { replace: true }) }} />
      {canExport && <button onClick={() => api.download(`/api/orgs/${orgId}/invoices/export?year=${year}&month=${month}`, `a-fatura_${year}-${String(month).padStart(2, '0')}.zip`)}>⬇ Exportar mês (ZIP)</button>}
    </>}>
      <Card>
        <Dropzone label="Carregar faturas (PDF ou fotografia)" accept="application/pdf,image/*" onFiles={upload} />
        <div className="row gap small">
          {(vehicles?.length ?? 0) > 0 && (
            <label className="inline">Viatura
              <select value={uploadVehicle} onChange={e => setUploadVehicle(e.target.value)}>
                <option value="">Detetar automaticamente</option>
                {vehicles!.filter(v => v.isActive).map(v => <option key={v.id} value={v.id}>{v.plate}</option>)}
              </select>
            </label>
          )}
          <label className="inline"><input type="checkbox" checked={paidPersonally} onChange={e => setPaidPersonally(e.target.checked)} /> Paguei do meu bolso (pedir reembolso)</label>
          {uploading && <span className="muted">{uploading}</span>}
        </div>
        <ErrorBox error={uploadError} />
      </Card>

      <div className="filters">
        <input placeholder="Pesquisar fornecedor, NIF, nº…" defaultValue={params.get('q') ?? ''} onKeyDown={e => e.key === 'Enter' && filter('q', (e.target as HTMLInputElement).value)} />
        <select value={params.get('categoryId') ?? ''} onChange={e => filter('categoryId', e.target.value)}>
          <option value="">Todas as categorias</option>
          {categories?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        {(vehicles?.length ?? 0) > 0 && (
          <select value={params.get('vehicleId') ?? ''} onChange={e => filter('vehicleId', e.target.value)}>
            <option value="">Todas as viaturas</option>
            {vehicles!.map(v => <option key={v.id} value={v.id}>{v.plate}</option>)}
          </select>
        )}
        <select value={params.get('status') ?? ''} onChange={e => filter('status', e.target.value)}>
          <option value="">Todos os estados</option>
          <option value="NeedsReview">Para rever</option>
          <option value="Processed">Processadas</option>
          <option value="Pending">Em fila</option>
          <option value="Failed">Com erro</option>
        </select>
        <select value={params.get('collected') ?? ''} onChange={e => filter('collected', e.target.value)}>
          <option value="">e-Fatura: todas</option>
          <option value="true">Recolhidas no e-Fatura</option>
          <option value="false">Não recolhidas</option>
        </select>
      </div>

      <ErrorBox error={error} />
      <Card right={data?.sums && <span className="muted small">{data.total} documentos · Base {eur(data.sums.net)} · IVA {eur(data.sums.vat)} · <b>Total {eur(data.sums.total)}</b></span>}>
        <table className="table">
          <thead>
            <tr><th>Data</th><th>Fornecedor</th><th>Documento</th><th>Categoria</th><th>Viatura</th><th className="num">IVA</th><th className="num">Total</th><th>e-Fatura</th><th>Estado</th><th>Origem</th></tr>
          </thead>
          <tbody>
            {data?.items.map(i => (
              <tr key={i.id}>
                <td>{date(i.issueDate)}</td>
                <td><Link to={`/o/${orgId}/invoices/${i.id}`}>{i.supplierName ?? i.fileName ?? '—'}</Link><div className="muted small">{i.supplierNif} · {i.uploadedByName}</div></td>
                <td className="small">{i.documentNumber ?? '—'}{i.duplicateOfId && <span className="pill s-Failed">duplicado</span>}</td>
                <td>{i.categoryName ? <span className="tag" style={{ borderColor: i.categoryColor }}>{i.categoryName}</span> : <span className="muted">—</span>}</td>
                <td>{i.vehiclePlate ?? ''}</td>
                <td className="num">{eur(i.vatAmount)}</td>
                <td className="num"><b>{eur(i.totalAmount)}</b></td>
                <td>{i.collectedInEFatura ? <span className="ok">✓ recolhida</span> : <span className="muted">—</span>}</td>
                <td><StatusPill status={i.status} />{i.flags.length > 0 && <span title={i.flags.join('\n')}> ⚠️</span>}</td>
                <td className="small muted">{SOURCE_LABEL[i.source] ?? i.source}</td>
              </tr>
            ))}
            {data?.items.length === 0 && <tr><td colSpan={10} className="center muted">Sem documentos neste período.</td></tr>}
          </tbody>
        </table>
      </Card>
    </Page>
  )
}
