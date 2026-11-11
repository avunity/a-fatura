import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type Category, type Invoice, type Project, type Vehicle } from '../api'
import { useLoad } from '../session'
import { Card, ErrorBox, Page, StatusPill, useOrgContext } from '../components/ui'
import { dateTime, eur, pct, SOURCE_LABEL } from '../util'

export default function InvoiceDetail() {
  const { orgId, id } = useParams()
  const { org } = useOrgContext()
  const nav = useNavigate()
  const { data: inv, reload, error } = useLoad(() => api.get<Invoice>(`/api/orgs/${orgId}/invoices/${id}`), [orgId, id])
  const { data: categories } = useLoad(() => api.get<Category[]>(`/api/orgs/${orgId}/categories`), [orgId])
  const { data: vehicles } = useLoad(() => api.get<Vehicle[]>(`/api/orgs/${orgId}/vehicles`), [orgId])
  const { data: projects } = useLoad(() => api.get<Project[]>(`/api/orgs/${orgId}/projects`), [orgId])
  const [fileUrl, setFileUrl] = useState<string | null>(null)
  const [form, setForm] = useState<Partial<Invoice>>({})
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!inv) return
    setForm(inv)
    let url: string | null = null
    api.blob(`/api/orgs/${orgId}/invoices/${id}/file`).then(b => { url = URL.createObjectURL(b); setFileUrl(url) }).catch(() => {})
    return () => { if (url) URL.revokeObjectURL(url) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inv?.id])

  useEffect(() => {
    if (inv && (inv.status === 'Pending' || inv.status === 'Processing')) {
      const t = setTimeout(reload, 3000)
      return () => clearTimeout(t)
    }
  }, [inv, reload])

  if (!inv) return <Page title="Fatura"><ErrorBox error={error} /></Page>
  const set = (k: keyof Invoice) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value })
  const num = (v: unknown) => (v === '' || v == null ? null : Number(v))
  const FISCAL: (keyof Invoice)[] = ['supplierNif', 'supplierName', 'buyerNif', 'documentType', 'documentNumber', 'atcud', 'issueDate', 'netAmount', 'vatAmount', 'totalAmount']
  const norm = (v: unknown) => (v === '' || v == null ? '' : String(v))
  const fiscalDirty = FISCAL.some(k => norm(form[k]) !== norm(inv[k]))
  const resetFiscal = () => setForm({ ...form, ...Object.fromEntries(FISCAL.map(k => [k, inv[k]])) })
  const totalsOff = num(form.netAmount) != null && num(form.vatAmount) != null && num(form.totalAmount) != null
    && Math.abs((num(form.netAmount)! + num(form.vatAmount)!) - num(form.totalAmount)!) > 0.05

  async function save(what: 'fiscal' | 'classification' = 'classification') {
    setErr(null); setMsg(null)
    try {
      await api.put(`/api/orgs/${orgId}/invoices/${id}`, {
        supplierNif: form.supplierNif, supplierName: form.supplierName, buyerNif: form.buyerNif,
        documentType: form.documentType, documentNumber: form.documentNumber, atcud: form.atcud,
        issueDate: form.issueDate || null, netAmount: num(form.netAmount), vatAmount: num(form.vatAmount), totalAmount: num(form.totalAmount),
        categoryId: form.categoryId || null, vehicleId: form.vehicleId || null, clearVehicle: !form.vehicleId,
        projectId: form.projectId || null, clearProject: !form.projectId,
        notes: form.notes, paidPersonally: form.paidPersonally,
      })
      setMsg(what === 'fiscal' ? 'Dados fiscais guardados.' : 'Guardado. A categoria/viatura escolhida fica memorizada para este fornecedor.')
      reload()
    } catch (e) { setErr((e as Error).message) }
  }

  const action = async (fn: () => Promise<unknown>) => { setErr(null); try { await fn(); reload() } catch (e) { setErr((e as Error).message) } }
  const isPdf = inv.fileContentType?.includes('pdf')

  return (
    <Page title={inv.supplierName ?? 'Fatura'} actions={<>
      <StatusPill status={inv.status} />
      <button onClick={() => action(() => api.post(`/api/orgs/${orgId}/invoices/${id}/reprocess`))}>↻ Reprocessar</button>
      {org?.access.canSeeAll && inv.status === 'NeedsReview' && <button className="primary" onClick={() => action(() => api.post(`/api/orgs/${orgId}/invoices/${id}/review`))}>✓ Marcar como revista</button>}
      <button className="danger" onClick={async () => { if (confirm('Apagar este documento?')) { await api.del(`/api/orgs/${orgId}/invoices/${id}`); nav(`/o/${orgId}/invoices`) } }}>Apagar</button>
    </>}>
      <ErrorBox error={err} />
      {msg && <div className="alert ok">{msg}</div>}
      {inv.flags.length > 0 && <div className="alert warn"><b>Atenção:</b><ul>{inv.flags.map(f => <li key={f}>{f}</li>)}</ul></div>}
      {inv.processingError && <div className="alert bad">{inv.processingError}</div>}

      <div className="grid2 detail">
        <Card title="Documento">
          {fileUrl ? (isPdf ? <iframe className="preview" src={fileUrl} title="documento" /> : <img className="preview" src={fileUrl} alt="documento" />) : <div className="muted">A carregar pré-visualização…</div>}
        </Card>
        <div>
          <Card title="Dados fiscais">
            <div className="form-grid">
              <label>NIF emitente<input value={form.supplierNif ?? ''} onChange={set('supplierNif')} /></label>
              <label>Emitente<input value={form.supplierName ?? ''} onChange={set('supplierName')} /></label>
              <label>NIF adquirente<input value={form.buyerNif ?? ''} onChange={set('buyerNif')} /></label>
              <label>Tipo<input value={form.documentType ?? ''} onChange={set('documentType')} placeholder="FT, FS, FR, NC…" /></label>
              <label>Número<input value={form.documentNumber ?? ''} onChange={set('documentNumber')} /></label>
              <label>ATCUD<input value={form.atcud ?? ''} onChange={set('atcud')} /></label>
              <label>Data<input type="date" value={form.issueDate ?? ''} onChange={set('issueDate')} /></label>
              <label>Base<input type="number" step="0.01" value={form.netAmount ?? ''} onChange={set('netAmount')} /></label>
              <label>IVA<input type="number" step="0.01" value={form.vatAmount ?? ''} onChange={set('vatAmount')} /></label>
              <label>Total<input type="number" step="0.01" value={form.totalAmount ?? ''} onChange={set('totalAmount')} /></label>
            </div>
            <div className={`save-row ${fiscalDirty ? 'dirty' : ''}`}>
              {fiscalDirty
                ? <span className="small warn-text">Alterações por guardar{totalsOff ? ' · base + IVA não corresponde ao total' : ''}</span>
                : <span className="small muted">Corrija os campos se a leitura automática falhou.</span>}
              <span className="spacer" />
              {fiscalDirty && <button onClick={resetFiscal}>Desfazer</button>}
              <button className="primary" disabled={!fiscalDirty} onClick={() => save('fiscal')}>Guardar</button>
            </div>
            {inv.vatLines.length > 0 && (
              <table className="table small">
                <thead><tr><th>Região</th><th>Taxa</th><th className="num">Base</th><th className="num">IVA</th></tr></thead>
                <tbody>{inv.vatLines.map((v, i) => <tr key={i}><td>{v.region}</td><td>{v.rate}%</td><td className="num">{eur(v.base)}</td><td className="num">{eur(v.vat)}</td></tr>)}</tbody>
              </table>
            )}
          </Card>
          {inv.subDocuments?.length > 1 && (
            <Card title={`Este documento contém ${inv.subDocuments.length} faturas`}>
              <p className="small muted">Cada fatura é ligada à sua linha do e-Fatura. Os totais acima são a soma de todas.</p>
              <table className="table small">
                <thead><tr><th>Data</th><th>Documento</th><th>ATCUD</th><th className="num">IVA</th><th className="num">Total</th></tr></thead>
                <tbody>{inv.subDocuments.map((d, k) => (
                  <tr key={k}><td>{d.issueDate ? new Date(d.issueDate).toLocaleDateString('pt-PT') : '—'}</td><td>{d.documentNumber}</td><td className="muted">{d.atcud}</td><td className="num">{eur(d.vat)}</td><td className="num">{eur(d.total)}</td></tr>
                ))}</tbody>
              </table>
            </Card>
          )}
          <Card title="Classificação">
            <div className="form-grid">
              <label>Categoria
                <select value={form.categoryId ?? ''} onChange={set('categoryId')}>
                  <option value="">—</option>
                  {categories?.filter(c => c.isActive).map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </label>
              <label>Viatura
                <select value={form.vehicleId ?? ''} onChange={set('vehicleId')}>
                  <option value="">—</option>
                  {vehicles?.map(v => <option key={v.id} value={v.id}>{v.plate} {v.make ?? ''}</option>)}
                </select>
              </label>
              <label className="span2">Projeto
                <select value={form.projectId ?? ''} onChange={set('projectId')}>
                  <option value="">Sem projeto</option>
                  {projects?.filter(p => p.isActive || p.id === form.projectId).map(p => <option key={p.id} value={p.id}>{p.name}{p.code ? ` (${p.code})` : ''}</option>)}
                </select>
              </label>
              <label className="span2">Notas<textarea value={form.notes ?? ''} onChange={set('notes')} rows={2} /></label>
              <label className="inline span2"><input type="checkbox" checked={!!form.paidPersonally} onChange={e => setForm({ ...form, paidPersonally: e.target.checked })} /> Pago pelo colaborador (reembolso: {inv.reimbursement})</label>
            </div>
            <p className="muted small">
              {inv.classificationReason} · confiança {pct(inv.classificationConfidence)}
              {inv.licensePlateDetected && <> · matrícula lida: <b>{inv.licensePlateDetected}</b></>}
              {inv.fuelLiters && <> · {inv.fuelLiters} L</>}{inv.energyKwh && <> · {inv.energyKwh} kWh</>}
            </p>
            <button className="primary" onClick={() => save('classification')}>Guardar</button>
            {org?.access.canManage && inv.paidPersonally && (
              <span className="row gap" style={{ marginTop: 8 }}>
                {['Approved', 'Paid', 'Rejected'].map(s => <button key={s} onClick={() => action(() => api.post(`/api/orgs/${orgId}/invoices/${id}/reimbursement`, { status: s }))}>{({ Approved: 'Aprovar', Paid: 'Marcar pago', Rejected: 'Rejeitar' } as Record<string, string>)[s]}</button>)}
              </span>
            )}
          </Card>
          <Card title="Rastreabilidade">
            <dl className="dl">
              <dt>Carregada por</dt><dd>{inv.uploadedByName} · {SOURCE_LABEL[inv.source]} · {dateTime(inv.createdAt)}</dd>
              <dt>Extração</dt><dd>{inv.extractionMethod}{inv.hasValidQr && ' · código QR AT lido'}</dd>
              <dt>e-Fatura</dt><dd>{inv.collectedInEFatura ? '✓ Recolhida (consta no e-Fatura)' : 'Não encontrada no e-Fatura importado'}</dd>
              {inv.duplicateOfId && <><dt>Duplicado de</dt><dd><a href={`/o/${orgId}/invoices/${inv.duplicateOfId}`}>ver original</a></dd></>}
              <dt>Revista</dt><dd>{inv.reviewedAt ? dateTime(inv.reviewedAt) : '—'}</dd>
              {inv.summary && <><dt>Resumo</dt><dd>{inv.summary}</dd></>}
            </dl>
          </Card>
        </div>
      </div>
    </Page>
  )
}
