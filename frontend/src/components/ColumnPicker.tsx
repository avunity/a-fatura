import { useEffect, useRef, useState } from 'react'
import { COLUMNS, defaultPrefs, type ColumnPref } from './invoiceColumns'
import { Icon } from './icons'

export default function ColumnPicker({ prefs, onChange }: { prefs: ColumnPref[]; onChange: (p: ColumnPref[]) => void }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const close = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [])

  const move = (idx: number, dir: -1 | 1) => {
    const j = idx + dir
    if (j < 0 || j >= prefs.length) return
    const next = [...prefs];
    [next[idx], next[j]] = [next[j]!, next[idx]!]
    onChange(next)
  }
  const toggle = (idx: number) => onChange(prefs.map((p, i) => (i === idx ? { ...p, visible: !p.visible } : p)))
  const visibleCount = prefs.filter(p => p.visible).length

  return (
    <div className="col-picker" ref={ref}>
      <button onClick={() => setOpen(!open)} title="Escolher e ordenar colunas"><Icon name="settings" size={15} /> Colunas <span className="muted small">{visibleCount}</span></button>
      {open && (
        <div className="popover col-popover">
          <div className="col-head"><b>Colunas</b><button className="link" onClick={() => onChange(defaultPrefs())}>Repor</button></div>
          {prefs.map((p, idx) => {
            const def = COLUMNS.find(c => c.id === p.id)!
            return (
              <div key={p.id} className="col-row">
                <label className="inline">
                  <input type="checkbox" checked={p.visible} disabled={def.locked} onChange={() => toggle(idx)} />
                  {def.label}{def.locked && <span className="muted small"> (fixa)</span>}
                </label>
                <span className="col-move">
                  <button className="icon-btn" disabled={idx === 0} onClick={() => move(idx, -1)} aria-label="Subir">↑</button>
                  <button className="icon-btn" disabled={idx === prefs.length - 1} onClick={() => move(idx, 1)} aria-label="Descer">↓</button>
                </span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
