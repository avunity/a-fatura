import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, type Category, type Invoice, type Project, type Vehicle } from '../api'
import { useLoad } from '../session'
import { Card, Dropzone, ErrorBox, Modal, Page, PeriodPicker, useOrgContext } from '../components/ui'
import ColumnPicker from '../components/ColumnPicker'
import { COLUMNS, loadPrefs, savePrefs, type ColumnId, type ColumnPref } from '../components/invoiceColumns'
import { eur } from '../util'

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
  const { data: projects } = useLoad(() => api.get<Project[]>(`/api/orgs/${orgId}/projects`), [orgId])
  const activeProjects = (projects ?? []).filter(p => p.isActive)

  const [uploadVehicle, setUploadVehicle] = useState('')
  const [uploadProject, setUploadProject] = useState('')
  // Rows shown immediately while the files are being sent; replaced by the real (Pending) rows once the server answers.
  const [sending, setSending] = useState<{ key: string; name: string; error?: string }[]>([])
  const [notice, setNotice] = useState<string | null>(null)
  const [paidPersonally, setPaidPersonally] = useState(false)
  const [uploading, setUploading] = useState<string | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)

  // ── Columns (order + visibility saved in the browser) ──
  const [prefs, setPrefsState] = useState<ColumnPref[]>(loadPrefs)
  const setPrefs = (p: ColumnPref[]) => { setPrefsState(p); savePrefs(p) }
  const visibleCols = prefs.filter(p => p.visible).map(p => COLUMNS.find(c => c.id === p.id)!).filter(Boolean)
  const visibleSet = useMemo(() => new Set<ColumnId>(visibleCols.map(c => c.id)), [prefs]) // eslint-disable-line react-hooks/exhaustive-deps
  const colCount = visibleCols.length + 1 // + selection column

  // ── Selection + bulk delete ──
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [bulkResult, setBulkResult] = useState<{ ok: string; skipped: { id: string; supplierName?: string; reason: string }[] } | null>(null)
  const items = data?.items ?? []
  // keep only ids still visible after filters change / reloads
  useEffect(() => {
    setSelected(cur => {
      const ids = new Set(items.map(i => i.id))
      const next = new Set([...cur].filter(id => ids.has(id)))
      return next.size === cur.size ? cur : next
    })
  }, [data]) // eslint-disable-line react-hooks/exhaustive-deps
  const allSelected = items.length > 0 && items.every(i => selected.has(i.id))
  const toggleAll = () => setSelected(allSelected ? new Set() : new Set(items.map(i => i.id)))
  const toggleOne = (id: string) => setSelected(cur => { const n = new Set(cur); if (n.has(id)) n.delete(id); else n.add(id); return n })
  const selectedItems = items.filter(i => selected.has(i.id))

  async function bulkDelete() {
    setDeleting(true)
    try {
      const r = await api.post<{ deleted: number; skipped: { id: string; supplierName?: string; reason: string }[]; notFound: number }>(
        `/api/orgs/${orgId}/invoices/bulk-delete`, { ids: [...selected] })
      setBulkResult({ ok: `${r.deleted} documento(s) apagado(s).`, skipped: r.skipped })
      setSelected(new Set())
      setConfirmDelete(false)
      await reload()
    } catch (e) {
      setBulkResult({ ok: '', skipped: [{ id: '', reason: (e as Error).message }] })
      setConfirmDelete(false)
    } finally { setDeleting(false) }
  }

  // Poll while documents are being processed
  const busy = data?.items.some(i => i.status === 'Pending' || i.status === 'Processing')
  useEffect(() => {
    if (!busy) return
    const t = setInterval(reload, 4000)
    return () => clearInterval(t)
  }, [busy, reload])

  async function upload(files: File[]) {
    if (files.length === 0) return
    setUploadError(null); setNotice(null)
    const batch = files.map((f, i) => ({ key: `${Date.now()}-${i}`, name: f.name }))
    setSending(cur => [...batch, ...cur])
    const form = new FormData()
    files.forEach(f => form.append('files', f))
    if (uploadVehicle) form.append('vehicleId', uploadVehicle)
    if (uploadProject) form.append('projectId', uploadProject)
    if (paidPersonally) form.append('paidPersonally', 'true')
    setUploading(`A enviar ${files.length} ficheiro(s)…`)
    try {
      await api.upload(`/api/orgs/${orgId}/invoices`, form)
      await reload()
      setSending(cur => cur.filter(x => !batch.some(b => b.key === x.key)))
      const now = new Date()
      if (year !== now.getFullYear() || (month !== undefined && month !== now.getMonth() + 1))
        setNotice(`${files.length} documento(s) enviados. Enquanto são processados aparecem em ${now.toLocaleDateString('pt-PT', { month: 'long', year: 'numeric' })}; depois ficam no mês da data da fatura.`)
    } catch (e) {
      const msg = (e as Error).message
      setUploadError(msg)
      setSending(cur => cur.map(x => (batch.some(b => b.key === x.key) ? { ...x, error: msg } : x)))
    } finally { setUploading(null) }
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
          {activeProjects.length > 0 && (
            <label className="inline">Projeto
              <select value={uploadProject} onChange={e => setUploadProject(e.target.value)}>
                <option value="">Sem projeto</option>
                {activeProjects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </label>
          )}
          <label className="inline"><input type="checkbox" checked={paidPersonally} onChange={e => setPaidPersonally(e.target.checked)} /> Paguei do meu bolso (pedir reembolso)</label>
          {uploading && <span className="muted">{uploading}</span>}
        </div>
        <ErrorBox error={uploadError} />
        {notice && <div className="alert ok">{notice}</div>}
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
        {(projects?.length ?? 0) > 0 && (
          <select value={params.get('projectId') ?? ''} onChange={e => filter('projectId', e.target.value)}>
            <option value="">Todos os projetos</option>
            {projects!.map(p => <option key={p.id} value={p.id}>{p.name}{p.isActive ? '' : ' (arquivado)'}</option>)}
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
      {bulkResult && (
        <div className={`alert ${bulkResult.skipped.length ? 'warn' : 'ok'}`}>
          {bulkResult.ok}
          {bulkResult.skipped.length > 0 && <ul>{bulkResult.skipped.map((x, k) => <li key={k}>{x.supplierName ? `${x.supplierName}: ` : ''}{x.reason}</li>)}</ul>}
          <button className="link" onClick={() => setBulkResult(null)}>fechar</button>
        </div>
      )}

      {selected.size > 0 && (
        <div className="bulk-bar">
          <span className="count">{selected.size} selecionada(s)</span>
          <span className="muted small">{eur(selectedItems.reduce((t, i) => t + (i.totalAmount ?? 0), 0))}</span>
          <span className="spacer" />
          <button className="link" onClick={() => setSelected(new Set())}>Limpar seleção</button>
          <button className="danger" onClick={() => setConfirmDelete(true)}>🗑 Apagar selecionadas</button>
        </div>
      )}

      <Card right={
        <div className="list-toolbar">
          {data?.sums && <span className="muted small">{data.total} documentos · Base {eur(data.sums.net)} · IVA {eur(data.sums.vat)} · <b>Total {eur(data.sums.total)}</b></span>}
          <ColumnPicker prefs={prefs} onChange={setPrefs} />
        </div>
      }>
        <div style={{ overflowX: 'auto' }}>
        <table className="table">
          <thead>
            <tr>
              <th className="sel"><input type="checkbox" checked={allSelected} onChange={toggleAll} disabled={items.length === 0} aria-label="Selecionar todas" /></th>
              {visibleCols.map(c => <th key={c.id} className={c.className?.includes('num') ? 'num' : ''}>{c.label}</th>)}
            </tr>
          </thead>
          <tbody>
            {sending.map(p => (
              <tr key={p.key} className="row-pending">
                <td className="sel" />
                <td colSpan={colCount - 1}>
                  <div className="row gap">
                    <span><b>{p.name}</b> <span className="muted small">a enviar…</span></span>
                    <span className="muted small" style={{ flex: 1 }}>{p.error ? <span className="bad-text">Falhou: {p.error}</span> : 'O documento vai ser lido (código QR + IA) assim que chegar ao servidor.'}</span>
                    {p.error
                      ? <button className="link" onClick={() => setSending(cur => cur.filter(x => x.key !== p.key))}>dispensar</button>
                      : <span className="pill s-Pending"><span className="spinner" />A enviar</span>}
                  </div>
                </td>
              </tr>
            ))}
            {items.map(i => (
              <tr key={i.id} className={selected.has(i.id) ? 'selected' : ''}>
                <td className="sel"><input type="checkbox" checked={selected.has(i.id)} onChange={() => toggleOne(i.id)} aria-label="Selecionar" /></td>
                {visibleCols.map(c => <td key={c.id} className={c.className}>{c.render(i, { orgId: orgId!, visible: visibleSet })}</td>)}
              </tr>
            ))}
            {items.length === 0 && sending.length === 0 && <tr><td colSpan={colCount} className="center muted">Sem documentos neste período.</td></tr>}
          </tbody>
        </table>
        </div>
      </Card>

      {confirmDelete && (
        <Modal title={`Apagar ${selected.size} documento(s)?`} onClose={() => !deleting && setConfirmDelete(false)}>
          <p>Os documentos são removidos da lista e deixam de contar para o e-Fatura e para a conciliação bancária. Esta ação não pode ser desfeita.</p>
          <ul className="small" style={{ maxHeight: 220, overflow: 'auto' }}>
            {selectedItems.slice(0, 50).map(i => <li key={i.id}>{i.supplierName ?? i.fileName ?? 'Documento'} {i.documentNumber ? `· ${i.documentNumber}` : ''} {i.totalAmount != null ? `· ${eur(i.totalAmount)}` : ''}</li>)}
            {selectedItems.length > 50 && <li className="muted">… e mais {selectedItems.length - 50}</li>}
          </ul>
          {!org?.access.canManage && <p className="small muted">Só pode apagar documentos carregados por si; os restantes serão ignorados.</p>}
          <div className="row gap">
            <button className="danger" disabled={deleting} onClick={bulkDelete}>{deleting ? 'A apagar…' : `Apagar ${selected.size}`}</button>
            <button disabled={deleting} onClick={() => setConfirmDelete(false)}>Cancelar</button>
          </div>
        </Modal>
      )}
    </Page>
  )
}
