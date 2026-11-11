import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Bars, Card, ErrorBox, Modal, Page, Stat } from '../components/ui'
import { date, dateTime } from '../util'
import { ColumnPicker, DataTable, useTableLayout, type Col } from '../components/DataTable'

type Stats = {
  users: { role: string; count: number }[]; organizations: { type: string; count: number }[]; invoices: number
  byStatus: { status: string; count: number }[]; byMethod: { method: string; count: number }[]; bySource: { source: string; count: number }[]
  daily: { day: string; count: number }[]; storageBytes: number
}
type Org = { id: string; name: string; nif?: string; type: string; isActive: boolean; createdAt: string; members: number; invoices: number; lastUpload?: string; accountants: { accountantUserId: string; name: string }[] }
type User = { id: string; email: string; name: string; role: string; isActive: boolean; lastLoginAt?: string; organizations: { organizationId: string; name: string; role: string }[]; clients: number }

export default function Backoffice() {
  const [tab, setTab] = useState<'overview' | 'orgs' | 'users'>('overview')
  const [q, setQ] = useState('')
  const { data: stats } = useLoad(() => api.get<Stats>('/api/backoffice/stats'), [])
  const { data: orgs, reload: reloadOrgs } = useLoad(() => api.get<Org[]>(`/api/backoffice/organizations?q=${encodeURIComponent(q)}`), [q])
  const { data: users, reload: reloadUsers } = useLoad(() => api.get<User[]>(`/api/backoffice/users?q=${encodeURIComponent(q)}`), [q])
  const [newUser, setNewUser] = useState<{ email: string; name: string; role: string } | null>(null)
  const [assign, setAssign] = useState<Org | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const orgColumns: Col<Org>[] = [
    { id: 'name', label: 'Entidade', width: 240, locked: true, render: o => <><Link to={`/o/${o.id}`}>{o.name}</Link><div className="muted small">{o.nif}</div></> },
    { id: 'type', label: 'Tipo', width: 110, render: o => o.type },
    { id: 'members', label: 'Membros', width: 90, className: 'num', render: o => o.members },
    { id: 'invoices', label: 'Docs', width: 80, className: 'num', render: o => o.invoices },
    { id: 'last', label: 'Último envio', width: 150, className: 'small', render: o => dateTime(o.lastUpload) },
    { id: 'created', label: 'Criada', width: 110, className: 'small', defaultVisible: false, render: o => date(o.createdAt) },
    { id: 'accountants', label: 'Contabilista', width: 220, className: 'small', render: o => <>{o.accountants.map(a => a.name).join(', ') || '—'} <button className="link" onClick={() => setAssign(o)}>associar</button></> },
    { id: 'actions', label: '', width: 100, render: o => <button className="link" onClick={async () => { await api.put(`/api/backoffice/organizations/${o.id}`, { isActive: !o.isActive }); reloadOrgs() }}>{o.isActive ? 'Suspender' : 'Reativar'}</button> },
  ]
  const userColumns: Col<User>[] = [
    { id: 'name', label: 'Nome', width: 220, locked: true, render: u => <>{u.name}<div className="muted small">{u.email}</div></> },
    { id: 'role', label: 'Perfil', width: 170, render: u => <>{u.role}{u.role === 'Accountant' && <span className="muted small"> · {u.clients} clientes</span>}</> },
    { id: 'orgs', label: 'Entidades', width: 280, className: 'small', render: u => u.organizations.map(o => `${o.name} (${o.role})`).join(', ') },
    { id: 'login', label: 'Último login', width: 150, className: 'small', render: u => dateTime(u.lastLoginAt) },
    {
      id: 'actions', label: '', width: 230, render: u => <>
        <button className="link" onClick={async () => { await api.put(`/api/backoffice/users/${u.id}`, { isActive: !u.isActive }); reloadUsers() }}>{u.isActive ? 'Bloquear' : 'Desbloquear'}</button>
        <button className="link" onClick={async () => { const r = await api.put<{ tempPassword: string }>(`/api/backoffice/users/${u.id}`, { resetPassword: true }); setInfo(`Nova palavra-passe temporária de ${u.email}: ${r.tempPassword}`) }}>Repor palavra-passe</button>
      </>,
    },
  ]
  const orgLayout = useTableLayout('backoffice-orgs', orgColumns)
  const userLayout = useTableLayout('backoffice-users', userColumns)
  const accountants = users?.filter(u => u.role === 'Accountant') ?? []
  const count = (arr: { count: number }[] | undefined) => arr?.reduce((s, x) => s + x.count, 0) ?? 0

  return (
    <Page title="Backoffice" actions={<>
      <div className="tabs inline">{(['overview', 'orgs', 'users'] as const).map(t => <button key={t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>{{ overview: 'Visão geral', orgs: 'Entidades', users: 'Utilizadores' }[t]}</button>)}</div>
      {tab !== 'overview' && <input placeholder="Pesquisar…" value={q} onChange={e => setQ(e.target.value)} />}
    </>}>
      <ErrorBox error={err} />
      {info && <div className="alert ok">{info}</div>}

      {tab === 'overview' && stats && (
        <>
          <div className="stats">
            <Stat label="Utilizadores" value={count(stats.users)} hint={stats.users.map(u => `${u.role}: ${u.count}`).join(' · ')} />
            <Stat label="Entidades" value={count(stats.organizations)} hint={stats.organizations.map(o => `${o.type}: ${o.count}`).join(' · ')} />
            <Stat label="Documentos" value={stats.invoices} />
            <Stat label="Armazenamento" value={`${(stats.storageBytes / 1024 / 1024).toFixed(1)} MB`} />
          </div>
          <div className="grid2">
            <Card title="Envios (30 dias)"><Bars items={stats.daily.map(d => ({ label: date(d.day), value: d.count }))} /></Card>
            <Card title="Método de extração"><Bars items={stats.byMethod.map(m => ({ label: m.method, value: m.count }))} /></Card>
            <Card title="Estado"><Bars items={stats.byStatus.map(m => ({ label: m.status, value: m.count }))} /></Card>
            <Card title="Origem"><Bars items={stats.bySource.map(m => ({ label: m.source, value: m.count }))} /></Card>
          </div>
        </>
      )}

      {tab === 'orgs' && (
        <Card right={<ColumnPicker layout={orgLayout} />}>
          <DataTable layout={orgLayout} rows={orgs ?? []} rowKey={o => o.id} rowClassName={o => (o.isActive ? '' : 'muted')} empty="Sem entidades." />
        </Card>
      )}

      {tab === 'users' && (
        <Card right={<div className="list-toolbar"><button className="primary" onClick={() => setNewUser({ email: '', name: '', role: 'Accountant' })}>+ Criar contabilista / admin</button><ColumnPicker layout={userLayout} /></div>}>
          <DataTable layout={userLayout} rows={users ?? []} rowKey={u => u.id} rowClassName={u => (u.isActive ? '' : 'muted')} empty="Sem utilizadores." />
        </Card>
      )}

      {newUser && (
        <Modal title="Criar utilizador" onClose={() => setNewUser(null)}>
          <div className="form-grid">
            <label>Nome<input value={newUser.name} onChange={e => setNewUser({ ...newUser, name: e.target.value })} /></label>
            <label>Email<input value={newUser.email} onChange={e => setNewUser({ ...newUser, email: e.target.value })} /></label>
            <label>Perfil<select value={newUser.role} onChange={e => setNewUser({ ...newUser, role: e.target.value })}><option value="Accountant">Contabilista</option><option value="Admin">Admin (backoffice)</option></select></label>
          </div>
          <button className="primary" onClick={async () => {
            try { const r = await api.post<{ email: string; tempPassword: string }>('/api/backoffice/users', newUser); setInfo(`Criado ${r.email} · palavra-passe temporária: ${r.tempPassword}`); setNewUser(null); reloadUsers() }
            catch (e) { setErr((e as Error).message) }
          }}>Criar</button>
        </Modal>
      )}

      {assign && (
        <Modal title={`Associar contabilista a ${assign.name}`} onClose={() => setAssign(null)}>
          {accountants.map(a => (
            <div key={a.id} className="row gap">
              <span>{a.name} <span className="muted small">{a.email}</span></span>
              <button onClick={async () => { await api.post('/api/backoffice/assignments', { organizationId: assign.id, accountantUserId: a.id }); setAssign(null); reloadOrgs() }}>Associar</button>
            </div>
          ))}
          {accountants.length === 0 && <p className="muted">Crie primeiro um utilizador contabilista.</p>}
        </Modal>
      )}
    </Page>
  )
}
