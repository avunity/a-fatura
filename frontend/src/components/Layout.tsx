import { NavLink, Outlet, useNavigate, useParams } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { api } from '../api'
import { useOrg, useSession } from '../session'

type Inbox = { total: number; organizations: { organizationId: string; unread: number }[] }

export default function Layout() {
  const { me, logout } = useSession()
  const { orgId } = useParams()
  const { org } = useOrg(orgId)
  const nav = useNavigate()
  const [inbox, setInbox] = useState<Inbox | null>(null)

  useEffect(() => {
    const load = () => api.get<Inbox>('/api/inbox').then(setInbox).catch(() => {})
    load()
    const t = setInterval(load, 60_000)
    return () => clearInterval(t)
  }, [])

  if (!me) return null
  const orgs = [
    ...me.memberships.map(m => ({ id: m.organizationId, name: m.name, tag: m.type === 'Individual' ? 'Pessoal' : m.role === 'Manager' ? 'Gestor' : 'Colaborador' })),
    ...me.clients.filter(c => !me.memberships.some(m => m.organizationId === c.organizationId)).map(c => ({ id: c.organizationId, name: c.name, tag: 'Cliente' })),
  ]
  const unread = (id?: string) => inbox?.organizations.find(o => o.organizationId === id)?.unread ?? 0
  const a = org?.access
  const isCompany = org?.type === 'Company'

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand" onClick={() => nav('/')}>A<span>€</span>-Fatura</div>

        {me.role === 'Accountant' && <NavLink to="/accountant" className="nav">📊 Painel de clientes</NavLink>}
        {me.role === 'Accountant' && me.clients.length === 0 && <div className="muted small" style={{ padding: '4px 10px' }}>Ainda sem clientes associados.</div>}
        {me.role === 'Admin' && <NavLink to="/backoffice" className="nav">🛠️ Backoffice</NavLink>}

        {orgs.length > 0 && (
          <select className="org-picker" value={orgId ?? ''} onChange={e => e.target.value && nav(`/o/${e.target.value}`)}>
            <option value="">— Escolher entidade —</option>
            {orgs.map(o => <option key={o.id} value={o.id}>{o.name} · {o.tag}</option>)}
          </select>
        )}

        {orgId && org && (
          <nav>
            <NavLink end to={`/o/${orgId}`} className="nav">🏠 Resumo</NavLink>
            <NavLink to={`/o/${orgId}/invoices`} className="nav">🧾 Faturas</NavLink>
            {a?.canSeeAll && <NavLink to={`/o/${orgId}/efatura`} className="nav">🏛️ e-Fatura</NavLink>}
            {a?.canSeeAll && <NavLink to={`/o/${orgId}/banks`} className="nav">🏦 Contas bancárias</NavLink>}
            {(a?.canManage || a?.canSeeAll) && <NavLink to={`/o/${orgId}/vehicles`} className="nav">🚗 Viaturas</NavLink>}
            <NavLink to={`/o/${orgId}/categories`} className="nav">🏷️ Categorias</NavLink>
            {isCompany && a?.canSeeAll && <NavLink to={`/o/${orgId}/team`} className="nav">👥 Colaboradores</NavLink>}
            <NavLink to={`/o/${orgId}/messages`} className="nav">
              ✉️ Mensagens {unread(orgId) > 0 && <span className="badge">{unread(orgId)}</span>}
            </NavLink>
            {(a?.canManage || a?.isAccountant) && <NavLink to={`/o/${orgId}/settings`} className="nav">⚙️ Definições</NavLink>}
          </nav>
        )}

        <div className="sidebar-foot">
          <div className="muted small">{me.name}</div>
          <div className="muted small">{me.email}</div>
          <NavLink to="/account" className="nav">👤 A minha conta</NavLink>
          <a href="/downloads/a-fatura.apk" className="nav">📱 App Android</a>
          <button className="link" onClick={logout}>Terminar sessão</button>
        </div>
      </aside>
      <main className="content">
        {me.mustChangePassword && <div className="alert warn">Está a usar uma palavra-passe temporária. <NavLink to="/account">Alterar agora</NavLink>.</div>}
        <Outlet context={{ org }} />
      </main>
    </div>
  )
}
