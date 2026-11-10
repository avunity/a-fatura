import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, ErrorBox, Modal, Page, useOrgContext } from '../components/ui'
import { dateTime } from '../util'

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
        <table className="table">
          <thead><tr><th>Nome</th><th>Perfil</th><th>Email para faturas</th><th>Documentos</th><th>Último envio</th><th></th></tr></thead>
          <tbody>{data?.map(m => (
            <tr key={m.id} className={m.isActive ? '' : 'muted'}>
              <td>{m.name}<div className="muted small">{m.email}</div></td>
              <td>{canManage
                ? <select value={m.role} onChange={e => update(m, { role: e.target.value })}><option value="Collaborator">Colaborador</option><option value="Manager">Gestor</option></select>
                : m.role}</td>
              <td className="small">{m.mailbox ? <code onClick={() => navigator.clipboard.writeText(m.mailbox!)} title="Copiar">{m.mailbox}</code> : <span className="muted">configure Mail:InboundDomain</span>}</td>
              <td>{m.invoices}</td>
              <td className="small">{dateTime(m.lastUpload)}</td>
              <td>{canManage && <>
                <button className="link" onClick={() => update(m, { isActive: !m.isActive })}>{m.isActive ? 'Desativar' : 'Ativar'}</button>
                <button className="link" onClick={async () => { await api.post(`/api/orgs/${orgId}/members/${m.id}/rotate-mailbox`); reload() }}>Novo email</button>
              </>}</td>
            </tr>))}
          </tbody>
        </table>
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
