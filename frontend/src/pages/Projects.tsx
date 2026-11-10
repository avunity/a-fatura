import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, type Project } from '../api'
import { useLoad } from '../session'
import { Bars, Card, ErrorBox, Modal, Page, Stat, useOrgContext } from '../components/ui'
import { date, eur, MONTHS, pct } from '../util'

type Edit = { id?: string; name: string; code: string; clientName: string; description: string; color: string; budget: string; startDate: string; endDate: string; isActive: boolean }
type Detail = {
  project: Project; spent: number; invoices: number; budgetUsed?: number
  byCategory: { name: string; color: string; total: number; count: number }[]
  bySupplier: { name: string; total: number; count: number }[]
  byMonth: { year: number; month: number; total: number }[]
}

const empty: Edit = { name: '', code: '', clientName: '', description: '', color: '#0f766e', budget: '', startDate: '', endDate: '', isActive: true }
const COLORS = ['#0f766e', '#2563eb', '#7c3aed', '#db2777', '#ea580c', '#ca8a04', '#16a34a', '#475569']

export default function Projects() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const { data, reload } = useLoad(() => api.get<Project[]>(`/api/orgs/${orgId}/projects`), [orgId])
  const [edit, setEdit] = useState<Edit | null>(null)
  const [open, setOpen] = useState<string | null>(null)
  const [showInactive, setShowInactive] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const canManage = !!org?.access.canManage
  const seeAll = !!org?.access.canSeeAll
  const { data: detail } = useLoad(() => open ? api.get<Detail>(`/api/orgs/${orgId}/projects/${open}`) : Promise.resolve(null), [orgId, open])

  async function save() {
    if (!edit) return
    setErr(null)
    const body = {
      ...edit, budget: edit.budget === '' ? null : Number(edit.budget),
      startDate: edit.startDate || null, endDate: edit.endDate || null,
      code: edit.code || null, clientName: edit.clientName || null, description: edit.description || null,
    }
    try {
      if (edit.id) await api.put(`/api/orgs/${orgId}/projects/${edit.id}`, body)
      else await api.post(`/api/orgs/${orgId}/projects`, body)
      setEdit(null); reload()
    } catch (e) { setErr((e as Error).message) }
  }

  const list = (data ?? []).filter(p => showInactive || p.isActive)
  const active = (data ?? []).filter(p => p.isActive)
  const totalSpent = active.reduce((s, p) => s + (p.spent ?? 0), 0)
  const overBudget = active.filter(p => (p.budgetUsed ?? 0) > 1).length

  return (
    <Page title="Projetos" actions={<>
      <label className="inline small"><input type="checkbox" checked={showInactive} onChange={e => setShowInactive(e.target.checked)} /> Mostrar arquivados</label>
      {canManage && <button className="primary" onClick={() => { setErr(null); setEdit({ ...empty }) }}>+ Novo projeto</button>}
    </>}>
      <p className="muted">Associe despesas a projetos (obras, clientes, centros de custo) no upload ou no detalhe da fatura, e acompanhe o gasto face ao orçamento.</p>

      {seeAll && (
        <div className="stats">
          <Stat label="Projetos ativos" value={active.length} />
          <Stat label="Gasto em projetos ativos" value={eur(totalSpent)} />
          <Stat label="Acima do orçamento" value={overBudget} tone={overBudget > 0 ? 'bad' : 'ok'} />
        </div>
      )}

      <div className="project-grid">
        {list.map(p => {
          const used = p.budgetUsed ?? 0
          return (
            <div key={p.id} className={`project-card ${p.isActive ? '' : 'archived'}`} onClick={() => seeAll && setOpen(p.id)}>
              <div className="project-top">
                <span className="project-dot" style={{ background: p.color }} />
                <div className="org-text">
                  <span className="org-name">{p.name}</span>
                  <span className="org-tag">{[p.code, p.clientName].filter(Boolean).join(' · ') || ' '}</span>
                </div>
                {!p.isActive && <span className="pill">arquivado</span>}
                {canManage && <button className="link" onClick={e => {
                  e.stopPropagation(); setErr(null)
                  setEdit({
                    id: p.id, name: p.name, code: p.code ?? '', clientName: p.clientName ?? '', description: p.description ?? '', color: p.color,
                    budget: p.budget?.toString() ?? '', startDate: p.startDate ?? '', endDate: p.endDate ?? '', isActive: p.isActive,
                  })
                }}>Editar</button>}
              </div>
              {seeAll && (
                <>
                  <div className="project-amount">{eur(p.spent)}{p.budget ? <span className="muted small"> / {eur(p.budget)}</span> : null}</div>
                  {p.budget ? (
                    <div className="progress"><span style={{ width: `${Math.min(used, 1) * 100}%`, background: used > 1 ? 'var(--bad)' : used > 0.85 ? 'var(--warn)' : p.color }} /></div>
                  ) : null}
                  <div className="row gap small muted">
                    <span>{p.invoices ?? 0} despesas</span>
                    {p.budget ? <span className={used > 1 ? 'bad-text' : ''}>{pct(used)} do orçamento</span> : null}
                    {(p.startDate || p.endDate) && <span>{date(p.startDate)} → {date(p.endDate)}</span>}
                  </div>
                </>
              )}
            </div>
          )
        })}
        {list.length === 0 && <div className="muted">Ainda não há projetos.{canManage && ' Crie o primeiro com "+ Novo projeto".'}</div>}
      </div>

      {open && detail && (
        <Modal title={detail.project.name} onClose={() => setOpen(null)}>
          <div className="stats">
            <Stat label="Gasto" value={eur(detail.spent)} hint={`${detail.invoices} despesas`} />
            {detail.project.budget ? <Stat label="Orçamento" value={eur(detail.project.budget)} hint={pct(detail.budgetUsed)} tone={(detail.budgetUsed ?? 0) > 1 ? 'bad' : 'ok'} /> : null}
          </div>
          {detail.project.description && <p className="pre muted">{detail.project.description}</p>}
          <Card title="Por categoria"><Bars items={detail.byCategory.map(c => ({ label: c.name, value: c.total, color: c.color, display: eur(c.total) }))} /></Card>
          <Card title="Principais fornecedores"><Bars items={detail.bySupplier.map(c => ({ label: c.name, value: c.total, display: eur(c.total) }))} /></Card>
          {detail.byMonth.length > 0 && (
            <Card title="Por mês"><Bars items={detail.byMonth.map(m => ({ label: `${MONTHS[m.month - 1].slice(0, 3)} ${m.year}`, value: m.total, display: eur(m.total), color: detail.project.color }))} /></Card>
          )}
          <Link to={`/o/${orgId}/invoices?projectId=${detail.project.id}&year=${new Date().getFullYear()}`}>Ver as faturas deste projeto →</Link>
        </Modal>
      )}

      {edit && (
        <Modal title={edit.id ? 'Editar projeto' : 'Novo projeto'} onClose={() => setEdit(null)}>
          <ErrorBox error={err} />
          <div className="form-grid">
            <label className="span2">Nome<input value={edit.name} onChange={e => setEdit({ ...edit, name: e.target.value })} autoFocus /></label>
            <label>Código<input value={edit.code} onChange={e => setEdit({ ...edit, code: e.target.value })} placeholder="ex: OB-2026-01" /></label>
            <label>Cliente<input value={edit.clientName} onChange={e => setEdit({ ...edit, clientName: e.target.value })} /></label>
            <label>Orçamento (€)<input type="number" step="0.01" value={edit.budget} onChange={e => setEdit({ ...edit, budget: e.target.value })} /></label>
            <label>Cor
              <span className="swatches">{COLORS.map(c => <button key={c} type="button" className={`swatch ${edit.color === c ? 'on' : ''}`} style={{ background: c }} onClick={() => setEdit({ ...edit, color: c })} />)}</span>
            </label>
            <label>Início<input type="date" value={edit.startDate} onChange={e => setEdit({ ...edit, startDate: e.target.value })} /></label>
            <label>Fim<input type="date" value={edit.endDate} onChange={e => setEdit({ ...edit, endDate: e.target.value })} /></label>
            <label className="span2">Descrição<textarea rows={3} value={edit.description} onChange={e => setEdit({ ...edit, description: e.target.value })} /></label>
            <label className="inline"><input type="checkbox" checked={edit.isActive} onChange={e => setEdit({ ...edit, isActive: e.target.checked })} /> Ativo</label>
          </div>
          <div className="row gap">
            <button className="primary" onClick={save}>Guardar</button>
            {edit.id && <button className="danger" onClick={async () => {
              if (!confirm('Arquivar/apagar este projeto? Se tiver despesas, fica arquivado e as despesas mantêm a associação.')) return
              await api.del(`/api/orgs/${orgId}/projects/${edit.id}`); setEdit(null); reload()
            }}>Arquivar</button>}
          </div>
        </Modal>
      )}
    </Page>
  )
}
