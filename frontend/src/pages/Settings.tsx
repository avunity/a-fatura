import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad, useSession } from '../session'
import { Card, ErrorBox, Page, useOrgContext } from '../components/ui'
import { MONTHS } from '../util'

type Period = { year: number; month: number; closedAt: string }

export default function Settings() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const { me, reload: reloadMe } = useSession()
  const [f, setF] = useState({ name: '', nif: '', address: '', email: '', submissionDeadlineDay: 10 })
  const [accEmail, setAccEmail] = useState('')
  const [pw, setPw] = useState({ currentPassword: '', newPassword: '' })
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const { data: periods, reload: reloadPeriods } = useLoad(() => api.get<Period[]>(`/api/orgs/${orgId}/periods`), [orgId])

  useEffect(() => {
    if (org) setF({ name: org.name, nif: org.nif ?? '', address: org.address ?? '', email: org.email ?? '', submissionDeadlineDay: org.submissionDeadlineDay })
  }, [org])

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setErr(null); setMsg(null)
    try { await fn(); setMsg(ok) } catch (e) { setErr((e as Error).message) }
  }
  const now = new Date()
  const recentMonths = Array.from({ length: 6 }, (_, i) => { const d = new Date(now.getFullYear(), now.getMonth() - i - 1, 1); return { year: d.getFullYear(), month: d.getMonth() + 1 } })
  const isClosed = (y: number, m: number) => periods?.some(p => p.year === y && p.month === m)

  return (
    <Page title="Definições">
      <ErrorBox error={err} />
      {msg && <div className="alert ok">{msg}</div>}
      <div className="grid2">
        {org?.access.canManage && (
          <Card title="Entidade">
            <div className="form-grid">
              <label>Nome<input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></label>
              <label>NIF<input value={f.nif} onChange={e => setF({ ...f, nif: e.target.value })} /></label>
              <label className="span2">Morada<input value={f.address} onChange={e => setF({ ...f, address: e.target.value })} /></label>
              <label>Email<input value={f.email} onChange={e => setF({ ...f, email: e.target.value })} /></label>
              <label>Prazo de entrega ao contabilista (dia do mês)<input type="number" min={1} max={28} value={f.submissionDeadlineDay} onChange={e => setF({ ...f, submissionDeadlineDay: Number(e.target.value) })} /></label>
            </div>
            <button className="primary" onClick={() => run(() => api.put(`/api/orgs/${orgId}`, { ...f, nif: f.nif || null }), 'Dados guardados')}>Guardar</button>
          </Card>
        )}

        <Card title="Contabilista">
          {org?.accountants.map(a => (
            <div key={a.accountantUserId} className="row gap">
              <span>🧮 {a.name} <span className="muted small">{a.email}</span></span>
              {org.access.canManage && <button className="link" onClick={() => run(() => api.del(`/api/orgs/${orgId}/accountant/${a.accountantUserId}`), 'Contabilista removido')}>remover</button>}
            </div>
          ))}
          {org?.accountants.length === 0 && <p className="muted">Nenhum contabilista associado.</p>}
          {org?.access.canManage && (
            <div className="row gap">
              <input placeholder="Email do contabilista (já registado no A-Fatura)" value={accEmail} onChange={e => setAccEmail(e.target.value)} />
              <button onClick={() => run(() => api.post(`/api/orgs/${orgId}/accountant`, { email: accEmail }), 'Contabilista associado')}>Associar</button>
            </div>
          )}
        </Card>

        <Card title="Fecho de períodos">
          <p className="small muted">Depois de fechado pelo contabilista, o mês não pode ser alterado pelo cliente.</p>
          <table className="table small"><tbody>
            {recentMonths.map(({ year, month }) => (
              <tr key={`${year}-${month}`}>
                <td>{MONTHS[month - 1]} {year}</td>
                <td>{isClosed(year, month) ? '🔒 Fechado' : 'Aberto'}</td>
                <td>{(org?.access.isAccountant || org?.access.isAdmin) && (isClosed(year, month)
                  ? <button className="link" onClick={() => run(async () => { await api.del(`/api/orgs/${orgId}/periods/${year}/${month}/close`); reloadPeriods() }, 'Período reaberto')}>Reabrir</button>
                  : <button className="link" onClick={() => run(async () => { await api.post(`/api/orgs/${orgId}/periods/${year}/${month}/close`); reloadPeriods() }, 'Período fechado')}>Fechar</button>)}</td>
              </tr>
            ))}
          </tbody></table>
        </Card>

        <Card title={`Conta · ${me?.email}`}>
          <div className="form-grid">
            <label>Palavra-passe atual<input type="password" value={pw.currentPassword} onChange={e => setPw({ ...pw, currentPassword: e.target.value })} /></label>
            <label>Nova palavra-passe<input type="password" minLength={8} value={pw.newPassword} onChange={e => setPw({ ...pw, newPassword: e.target.value })} /></label>
          </div>
          <button onClick={() => run(async () => { await api.post('/api/auth/change-password', pw); setPw({ currentPassword: '', newPassword: '' }); reloadMe() }, 'Palavra-passe alterada')}>Alterar palavra-passe</button>
        </Card>
      </div>
    </Page>
  )
}
