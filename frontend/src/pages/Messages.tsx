import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, Page } from '../components/ui'
import { dateTime } from '../util'

type Msg = { id: string; parentId?: string; subject: string; body: string; fromAccountant: boolean; createdAt: string; senderName: string; read: boolean }

export default function Messages() {
  const { orgId } = useParams()
  const { data, reload } = useLoad(() => api.get<Msg[]>(`/api/orgs/${orgId}/messages`), [orgId])
  const [open, setOpen] = useState<string | null>(null)
  const [draft, setDraft] = useState({ subject: '', body: '' })
  const [reply, setReply] = useState('')

  useEffect(() => { if (data?.some(m => !m.read)) api.post(`/api/orgs/${orgId}/messages/read`).catch(() => {}) }, [data, orgId])

  const threads = (data ?? []).filter(m => !m.parentId)
  const repliesOf = (id: string) => (data ?? []).filter(m => m.parentId === id).sort((a, b) => a.createdAt.localeCompare(b.createdAt))

  async function send(parentId?: string) {
    const body = parentId ? { subject: '', body: reply, parentId } : draft
    if (!body.body.trim()) return
    await api.post(`/api/orgs/${orgId}/messages`, body)
    setDraft({ subject: '', body: '' }); setReply('')
    reload()
  }

  return (
    <Page title="Mensagens">
      <Card title="Nova mensagem">
        <input placeholder="Assunto" value={draft.subject} onChange={e => setDraft({ ...draft, subject: e.target.value })} />
        <textarea placeholder="Escreva a mensagem…" rows={3} value={draft.body} onChange={e => setDraft({ ...draft, body: e.target.value })} />
        <button className="primary" onClick={() => send()}>Enviar</button>
      </Card>
      {threads.map(t => (
        <Card key={t.id} title={t.subject} right={<span className="muted small">{t.fromAccountant ? '🧮 Contabilista' : '🏢 Cliente'} · {t.senderName} · {dateTime(t.createdAt)} {!t.read && <span className="badge">nova</span>}</span>}>
          <p className="pre">{t.body}</p>
          {repliesOf(t.id).map(r => (
            <div key={r.id} className={`reply ${r.fromAccountant ? 'acc' : ''}`}>
              <div className="muted small">{r.senderName} · {dateTime(r.createdAt)}</div>
              <p className="pre">{r.body}</p>
            </div>
          ))}
          {open === t.id ? (
            <div><textarea rows={2} value={reply} onChange={e => setReply(e.target.value)} autoFocus /><button className="primary" onClick={() => send(t.id)}>Responder</button></div>
          ) : <button className="link" onClick={() => { setOpen(t.id); setReply('') }}>Responder</button>}
        </Card>
      ))}
      {threads.length === 0 && <p className="muted">Sem mensagens.</p>}
    </Page>
  )
}
