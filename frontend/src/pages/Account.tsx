import { useState } from 'react'
import { api } from '../api'
import { useSession } from '../session'
import { Card, ErrorBox, Page } from '../components/ui'
import ThemeSwitch from '../components/ThemeSwitch'

/** Personal account page — available to every profile, with or without an organization selected. */
export default function Account() {
  const { me, reload } = useSession()
  const [pw, setPw] = useState({ currentPassword: '', newPassword: '', confirm: '' })
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  async function change() {
    setErr(null); setMsg(null)
    if (pw.newPassword !== pw.confirm) { setErr('As palavras-passe novas não coincidem'); return }
    try {
      await api.post('/api/auth/change-password', { currentPassword: pw.currentPassword, newPassword: pw.newPassword })
      setPw({ currentPassword: '', newPassword: '', confirm: '' })
      setMsg('Palavra-passe alterada.')
      reload()
    } catch (e) { setErr((e as Error).message) }
  }

  const roleLabel: Record<string, string> = { User: 'Utilizador', Accountant: 'Contabilista', Admin: 'Backoffice' }

  return (
    <Page title="A minha conta">
      <Card title="Dados">
        <dl className="dl">
          <dt>Nome</dt><dd>{me?.name}</dd>
          <dt>Email</dt><dd>{me?.email}</dd>
          <dt>Perfil</dt><dd>{roleLabel[me?.role ?? ''] ?? me?.role}</dd>
          {me && me.memberships.length > 0 && <><dt>Entidades</dt><dd>{me.memberships.map(m => m.name).join(', ')}</dd></>}
          {me && me.clients.length > 0 && <><dt>Clientes</dt><dd>{me.clients.length}</dd></>}
        </dl>
      </Card>
      <Card title="Aparência">
        <p className="small muted">"Sistema" segue o modo claro/escuro do seu computador ou telemóvel.</p>
        <div style={{ maxWidth: 360 }}><ThemeSwitch /></div>
      </Card>
      <Card title="Alterar palavra-passe">
        <ErrorBox error={err} />
        {msg && <div className="alert ok">{msg}</div>}
        <div className="form-grid">
          <label className="span2">Palavra-passe atual<input type="password" value={pw.currentPassword} onChange={e => setPw({ ...pw, currentPassword: e.target.value })} /></label>
          <label>Nova palavra-passe (mín. 8)<input type="password" minLength={8} value={pw.newPassword} onChange={e => setPw({ ...pw, newPassword: e.target.value })} /></label>
          <label>Repetir nova palavra-passe<input type="password" minLength={8} value={pw.confirm} onChange={e => setPw({ ...pw, confirm: e.target.value })} /></label>
        </div>
        <button className="primary" onClick={change}>Alterar palavra-passe</button>
      </Card>
    </Page>
  )
}
