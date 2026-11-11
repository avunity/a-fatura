import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, ErrorBox, Modal, Page, useOrgContext } from '../components/ui'
import { dateTime } from '../util'
import { ColumnPicker, DataTable, useTableLayout, type Col } from '../components/DataTable'

type Member = { id: string; userId: string; name: string; email: string; role: string; isActive: boolean; mailbox?: string; invoices: number; lastUpload?: string }

export default function Team() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const { data, reload } = useLoad(() => api.get<Member[]>(`/api/orgs/${orgId}/members`), [orgId])
  const [add, setAdd] = useState<{ name: string; email: string; role: string } | null>(null)
  const [created, setCreated] = useState<{ email: string; tempPassword?: string } | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const canManage = org?.access.canManage

  const update = async (m: Member, body: object) => {
    setErr(null)
    try { await api.put(`/api/orgs/${orgId}/members/${m.id}`, body); reload() } catch (e) { setErr((e as Error).message) }
  }
  const columns = useMemo<Col<Member>[]>(() => [
    { id: 'name', label: 'Nome', sort: m => m.name, width: 240, locked: true, render: m => <>{m.name}<div className="muted small">{m.email}</div></> },
    {
      id: 'role', label: 'Perfil', sort: m => m.role, width: 150,
      render: m => canManage
        ? <select value={m.role} onChange={e => update(m, { role: e.target.value })}><option value="Collaborator">Colaborador</option><option value="Manager">Gestor</option></select>
        : (m.role === 'Manager' ? 'Gestor' : 'Colaborador'),
    },
    { id: 'mailbox', label: 'Email para faturas', sort: m => m.mailbox, width: 300, className: 'small', render: m => m.mailbox ? <code onClick={() => navigator.clipboard.writeText(m.mailbox!)} title="Copiar">{m.mailbox}</code> : <span className="muted">configure Mail:InboundDomain</span> },
    { id: 'invoices', label: 'Documentos', sort: m => m.invoices, width: 110, className: 'num', render: m => m.invoices },
    { id: 'last', label: 'Último envio', sort: m => m.lastUpload, width: 140, className: 'small', render: m => dateTime(m.lastUpload) },
    {
      id: 'actions', label: 'Ações', width: 180, className: 'row-actions',
      render: m => canManage && <>
        <button className="link" onClick={() => update(m, { isActive: !m.isActive })}>{m.isActive ? 'Desativar' : 'Ativar'}</button>
        <button className="link" onClick={async () => { await api.post(`/api/orgs/${orgId}/members/${m.id}/rotate-mailbox`); reload() }}>Novo email</button>
      </>,
    },
  ], [canManage, orgId]) // eslint-disable-line react-hooks/exhaustive-deps
  const layout = useTableLayout('team', columns)



  return (
    <Page title="Colaboradores" actions={canManage && <button className="primary" onClick={() => setAdd({ name: '', email: '', role: 'Collaborator' })}>+ Adicionar colaborador</button>}>
      <p className="muted">Cada colaborador tem um endereço de email próprio: basta reencaminhar faturas para lá e ficam carregadas em seu nome. Colaboradores só veem os documentos que carregaram; gestores veem tudo.</p>
      <ErrorBox error={err} />
      {created && (
        <div className="alert ok">
          Colaborador {created.email} adicionado.
          {created.tempPassword && <> Palavra-passe temporária: <code>{created.tempPassword}</code> (mostrada apenas agora — envie-a ao colaborador).</>}
        </div>
      )}
      <Card>
<DataTable layout={layout} rows={data ?? []} rowKey={m => m.id} rowClassName={m => (m.isActive ? '' : 'muted')} empty="Sem colaboradores." />
      </Card>

      {add && (
        <Modal title="Adicionar colaborador" onClose={() => setAdd(null)}>
          <div className="form-grid">
            <label>Nome<input value={add.name} onChange={e => setAdd({ ...add, name: e.target.value })} /></label>
            <label>Email<input type="email" value={add.email} onChange={e => setAdd({ ...add, email: e.target.value })} /></label>
            <label>Perfil<select value={add.role} onChange={e => setAdd({ ...add, role: e.target.value })}><option value="Collaborator">Colaborador</option><option value="Manager">Gestor</option></select></label>
          </div>
          <button className="primary" onClick={async () => {
            try { setCreated(await api.post(`/api/orgs/${orgId}/members`, add)); setAdd(null); reload() } catch (e) { setErr((e as Error).message); setAdd(null) }
          }}>Adicionar</button>
        </Modal>
      )}
    </Page>
  )
}
