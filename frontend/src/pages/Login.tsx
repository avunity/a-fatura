import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, auth } from '../api'

export default function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      const r = await api.post<{ token: string }>('/api/auth/login', { email, password })
      auth.set(r.token)
      location.href = '/'
    } catch (err) { setError((err as Error).message) } finally { setBusy(false) }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <div className="brand big">A<span>€</span>-Fatura</div>
        <p className="muted">Faturas e despesas organizadas, prontas para o contabilista.</p>
        {error && <div className="alert bad">{error}</div>}
        <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required autoFocus /></label>
        <label>Palavra-passe<input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></label>
        <button className="primary" disabled={busy}>{busy ? 'A entrar…' : 'Entrar'}</button>
        <p className="small">Ainda não tem conta? <Link to="/register">Criar conta</Link></p>
      </form>
    </div>
  )
}
