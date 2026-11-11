import { NavLink, Outlet, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useEffect, useRef, useState } from 'react'
import { api } from '../api'
import { useOrg, useSession } from '../session'
import { Icon, type IconName } from './icons'
import ThemeSwitch from './ThemeSwitch'

type Inbox = { total: number; organizations: { organizationId: string; unread: number }[] }
type OrgOption = { id: string; name: string; tag: string }
type Item = { to: string; label: string; icon: IconName; end?: boolean; badge?: number; show?: boolean }

const initials = (s: string) => s.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]!.toUpperCase()).join('')

export default function Layout() {
  const { me, logout } = useSession()
  const { orgId } = useParams()
  const { org } = useOrg(orgId)
  const nav = useNavigate()
  const location = useLocation()
  const [inbox, setInbox] = useState<Inbox | null>(null)
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem('afatura.sidebar') === 'collapsed')
  const [mobileOpen, setMobileOpen] = useState(false)
  const [orgMenu, setOrgMenu] = useState(false)
  const [userMenu, setUserMenu] = useState(false)
  const [orgSearch, setOrgSearch] = useState('')
  const orgRef = useRef<HTMLDivElement>(null)
  const userRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const load = () => api.get<Inbox>('/api/inbox').then(setInbox).catch(() => {})
    load()
    const t = setInterval(load, 60_000)
    return () => clearInterval(t)
  }, [])

  useEffect(() => { setMobileOpen(false); setOrgMenu(false); setUserMenu(false) }, [location.pathname])
  useEffect(() => { localStorage.setItem('afatura.sidebar', collapsed ? 'collapsed' : 'open') }, [collapsed])
  useEffect(() => {
    const close = (e: MouseEvent) => {
      if (orgRef.current && !orgRef.current.contains(e.target as Node)) setOrgMenu(false)
      if (userRef.current && !userRef.current.contains(e.target as Node)) setUserMenu(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [])

  if (!me) return null

  const orgs: OrgOption[] = [
    ...me.memberships.map(m => ({ id: m.organizationId, name: m.name, tag: m.type === 'Individual' ? 'Pessoal' : m.role === 'Manager' ? 'Gestor' : 'Colaborador' })),
    ...me.clients.filter(c => !me.memberships.some(m => m.organizationId === c.organizationId)).map(c => ({ id: c.organizationId, name: c.name, tag: 'Cliente' })),
  ]
  const current = orgs.find(o => o.id === orgId) ?? (org ? { id: org.id, name: org.name, tag: org.access.isAdmin ? 'Backoffice' : '' } : null)
  const unread = (id?: string) => inbox?.organizations.find(o => o.organizationId === id)?.unread ?? 0
  const a = org?.access
  const isCompany = org?.type === 'Company'
  const base = `/o/${orgId}`

  const sections: { title: string; items: Item[] }[] = []
  if (me.role === 'Accountant' || me.role === 'Admin')
    sections.push({
      title: me.role === 'Admin' ? 'Plataforma' : 'Contabilista',
      items: [
        { to: '/accountant', label: 'Painel de clientes', icon: 'chart', show: me.role === 'Accountant' },
        { to: '/backoffice', label: 'Backoffice', icon: 'shield', show: me.role === 'Admin' },
      ],
    })
  if (orgId && org) {
    sections.push({
      title: 'Geral', items: [
        { to: base, label: 'Resumo', icon: 'home', end: true },
        { to: `${base}/invoices`, label: 'Faturas', icon: 'invoice' },
        { to: `${base}/messages`, label: 'Mensagens', icon: 'mail', badge: unread(orgId) },
      ],
    })
    sections.push({
      title: 'Finanças', items: [
        { to: `${base}/projects`, label: 'Projetos', icon: 'folder' },
        { to: `${base}/efatura`, label: 'e-Fatura', icon: 'gov', show: a?.canSeeAll },
        { to: `${base}/banks`, label: 'Contas bancárias', icon: 'bank', show: a?.canSeeAll },
      ],
    })
    sections.push({
      title: 'Gestão', items: [
        { to: `${base}/vehicles`, label: 'Viaturas', icon: 'car', show: a?.canManage || a?.canSeeAll },
        { to: `${base}/categories`, label: 'Categorias', icon: 'tag' },
        { to: `${base}/team`, label: 'Colaboradores', icon: 'users', show: isCompany && a?.canSeeAll },
        { to: `${base}/settings`, label: 'Definições', icon: 'settings', show: a?.canManage || a?.isAccountant },
      ],
    })
  }

  const filteredOrgs = orgs.filter(o => o.name.toLowerCase().includes(orgSearch.toLowerCase()))

  return (
    <div className={`shell ${collapsed ? 'collapsed' : ''}`}>
      <header className="topbar">
        <button className="icon-btn" onClick={() => setMobileOpen(true)} aria-label="Abrir menu"><Icon name="menu" /></button>
        <div className="brand-mark small" onClick={() => nav('/')}>A€</div>
        <span className="topbar-title">{current?.name ?? 'A-Fatura'}</span>
      </header>
      {mobileOpen && <div className="scrim" onClick={() => setMobileOpen(false)} />}

      <aside className={`sidebar ${mobileOpen ? 'open' : ''}`}>
        <div className="sb-head">
          <div className="brand" onClick={() => nav('/')}>
            <div className="brand-mark">A€</div>
            <span className="brand-name">A-Fatura</span>
          </div>
          <button className="icon-btn hide-mobile" onClick={() => setCollapsed(!collapsed)} title={collapsed ? 'Expandir' : 'Recolher'}>
            <Icon name={collapsed ? 'expand' : 'collapse'} />
          </button>
          <button className="icon-btn show-mobile" onClick={() => setMobileOpen(false)} aria-label="Fechar"><Icon name="close" /></button>
        </div>

        {orgs.length > 0 && (
          <div className="org-switch" ref={orgRef}>
            <button className="org-current" onClick={() => setOrgMenu(!orgMenu)} title={current?.name}>
              <span className="avatar">{current ? initials(current.name) : '?'}</span>
              <span className="org-text">
                <span className="org-name">{current?.name ?? 'Escolher entidade'}</span>
                {current?.tag && <span className="org-tag">{current.tag}</span>}
              </span>
              <Icon name="chevron" size={16} />
            </button>
            {orgMenu && (
              <div className="popover">
                {orgs.length > 6 && (
                  <div className="popover-search"><Icon name="search" size={16} /><input autoFocus placeholder="Procurar…" value={orgSearch} onChange={e => setOrgSearch(e.target.value)} /></div>
                )}
                <div className="popover-list">
                  {filteredOrgs.map(o => (
                    <button key={o.id} className={`popover-item ${o.id === orgId ? 'on' : ''}`} onClick={() => { setOrgMenu(false); nav(`/o/${o.id}`) }}>
                      <span className="avatar sm">{initials(o.name)}</span>
                      <span className="org-text"><span className="org-name">{o.name}</span><span className="org-tag">{o.tag}</span></span>
                      {unread(o.id) > 0 && <span className="badge">{unread(o.id)}</span>}
                      {o.id === orgId && <Icon name="check" size={16} />}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        <nav className="sb-nav">
          {sections.map(sec => {
            const items = sec.items.filter(i => i.show !== false)
            if (items.length === 0) return null
            return (
              <div key={sec.title} className="sb-section">
                <div className="sb-title">{sec.title}</div>
                {items.map(i => (
                  <NavLink key={i.to} to={i.to} end={i.end} className="nav" title={collapsed ? i.label : undefined}>
                    <Icon name={i.icon} />
                    <span className="nav-label">{i.label}</span>
                    {!!i.badge && <span className="badge">{i.badge}</span>}
                  </NavLink>
                ))}
              </div>
            )
          })}
          {me.role === 'Accountant' && me.clients.length === 0 && !orgId && <div className="sb-hint">Ainda sem clientes associados.</div>}
        </nav>

        <div className="sb-foot">
          <a href="/downloads/a-fatura.apk" className="nav" title={collapsed ? 'App Android' : undefined}>
            <Icon name="phone" /><span className="nav-label">App Android</span>
          </a>
          <div className="user-card" ref={userRef}>
            <button className="user-btn" onClick={() => setUserMenu(!userMenu)} title={collapsed ? me.name : undefined}>
              <span className="avatar user">{initials(me.name || me.email)}</span>
              <span className="org-text"><span className="org-name">{me.name}</span><span className="org-tag">{me.email}</span></span>
            </button>
            {userMenu && (
              <div className="popover up">
                <ThemeSwitch />
                <button className="popover-item" onClick={() => nav('/account')}><Icon name="user" size={16} /> A minha conta</button>
                <button className="popover-item danger" onClick={logout}><Icon name="logout" size={16} /> Terminar sessão</button>
              </div>
            )}
          </div>
        </div>
      </aside>

      <main className="content">
        {me.mustChangePassword && <div className="alert warn">Está a usar uma palavra-passe temporária. <NavLink to="/account">Alterar agora</NavLink>.</div>}
        <Outlet context={{ org }} />
      </main>
    </div>
  )
}
