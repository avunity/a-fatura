import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, auth } from '../api'

export default function Register() {
  const [f, setF] = useState({ name: '', email: '', password: '', accountType: 'Company', organizationName: '', nif: '' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      const r = await api.post<{ token: string }>('/api/auth/register', { ...f, nif: f.nif || null })
      auth.set(r.token)
      location.href = '/'
    } catch (err) { setError((err as Error).message) } finally { setBusy(false) }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <div className="brand big">A<span>€</span>-Fatura</div>
        <div className="segmented">
          <button type="button" className={f.accountType === 'Company' ? 'on' : ''} onClick={() => setF({ ...f, accountType: 'Company' })}>Empresa</button>
          <button type="button" className={f.accountType === 'Individual' ? 'on' : ''} onClick={() => setF({ ...f, accountType: 'Individual' })}>Particular / ENI</button>
        </div>
        {error && <div className="alert bad">{error}</div>}
        <label>O seu nome<input value={f.name} onChange={set('name')} required /></label>
        <label>Email<input type="email" value={f.email} onChange={set('email')} required /></label>
        <label>Palavra-passe<input type="password" minLength={8} value={f.password} onChange={set('password')} required /></label>
        {f.accountType === 'Company' && <label>Nome da empresa<input value={f.organizationName} onChange={set('organizationName')} required /></label>}
        <label>NIF {f.accountType === 'Company' ? 'da empresa' : ''}<input value={f.nif} onChange={set('nif')} inputMode="numeric" placeholder="9 dígitos" /></label>
        <button className="primary" disabled={busy}>{busy ? 'A criar…' : 'Criar conta'}</button>
        <p className="small">Já tem conta? <Link to="/login">Entrar</Link></p>
      </form>
    </div>
  )
}
