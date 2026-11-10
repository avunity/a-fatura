import { useRef, useState, type ReactNode } from 'react'
import { useOutletContext } from 'react-router-dom'
import type { OrgInfo } from '../api'
import { MONTHS } from '../util'

export const useOrgContext = () => useOutletContext<{ org: OrgInfo | null }>()

export function Page({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <div className="page">
      <header className="page-head">
        <h1>{title}</h1>
        <div className="actions">{actions}</div>
      </header>
      {children}
    </div>
  )
}

export function Card({ title, children, right }: { title?: string; children: ReactNode; right?: ReactNode }) {
  return (
    <section className="card">
      {(title || right) && <div className="card-head"><h3>{title}</h3>{right}</div>}
      {children}
    </section>
  )
}

export function Stat({ label, value, hint, tone }: { label: string; value: ReactNode; hint?: string; tone?: 'ok' | 'warn' | 'bad' }) {
  return (
    <div className={`stat ${tone ?? ''}`}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
      {hint && <div className="stat-hint">{hint}</div>}
    </div>
  )
}

export function Bars({ items }: { items: { label: string; value: number; color?: string; display?: string }[] }) {
  const max = Math.max(1, ...items.map(i => i.value))
  return (
    <div className="bars">
      {items.map(i => (
        <div key={i.label} className="bar-row">
          <span className="bar-label" title={i.label}>{i.label}</span>
          <span className="bar-track"><span className="bar-fill" style={{ width: `${(i.value / max) * 100}%`, background: i.color ?? 'var(--accent)' }} /></span>
          <span className="bar-value">{i.display ?? i.value}</span>
        </div>
      ))}
    </div>
  )
}

export function PeriodPicker({ year, month, onChange, allowAllMonths }: {
  year: number; month?: number; onChange: (y: number, m?: number) => void; allowAllMonths?: boolean
}) {
  const years = Array.from({ length: 6 }, (_, i) => new Date().getFullYear() - i)
  return (
    <span className="period">
      <select value={month ?? ''} onChange={e => onChange(year, e.target.value ? Number(e.target.value) : undefined)}>
        {allowAllMonths && <option value="">Ano inteiro</option>}
        {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
      </select>
      <select value={year} onChange={e => onChange(Number(e.target.value), month)}>
        {years.map(y => <option key={y} value={y}>{y}</option>)}
      </select>
    </span>
  )
}

export function Dropzone({ onFiles, accept, label, multiple = true }: {
  onFiles: (files: File[]) => void; accept?: string; label: string; multiple?: boolean
}) {
  const input = useRef<HTMLInputElement>(null)
  const [over, setOver] = useState(false)
  return (
    <div
      className={`dropzone ${over ? 'over' : ''}`}
      onClick={() => input.current?.click()}
      onDragOver={e => { e.preventDefault(); setOver(true) }}
      onDragLeave={() => setOver(false)}
      onDrop={e => { e.preventDefault(); setOver(false); onFiles(Array.from(e.dataTransfer.files)) }}
    >
      <input ref={input} type="file" hidden multiple={multiple} accept={accept}
        onChange={e => { onFiles(Array.from(e.target.files ?? [])); e.target.value = '' }} />
      <div>📎 {label}</div>
      <div className="muted small">Arraste para aqui ou clique para escolher</div>
    </div>
  )
}

export function StatusPill({ status }: { status: string }) {
  const label: Record<string, string> = { Pending: 'Em fila', Processing: 'A processar', Processed: 'Processada', NeedsReview: 'Rever', Failed: 'Erro' }
  const busy = status === 'Pending' || status === 'Processing'
  return <span className={`pill s-${status}`}>{busy && <span className="spinner" />}{label[status] ?? status}</span>
}

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="card-head"><h3>{title}</h3><button className="link" onClick={onClose}>✕</button></div>
        {children}
      </div>
    </div>
  )
}

export function ErrorBox({ error }: { error?: string | null }) {
  return error ? <div className="alert bad">{error}</div> : null
}
