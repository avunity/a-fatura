import { useEffect, useMemo, useRef, useState, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react'
import { Icon } from './icons'

/**
 * Reusable list table: columns can be reordered (drag the header), resized (drag the header's right edge),
 * shown/hidden (ColumnPicker). The layout is saved per table in the browser.
 */
export type Col<T> = {
  id: string
  label: string
  render: (row: T, ctx: { visible: Set<string> }) => ReactNode
  className?: string          // applied to the cell (e.g. 'num', 'small')
  defaultVisible?: boolean    // default true
  locked?: boolean            // cannot be hidden
  width?: number              // default width in px (otherwise shares the remaining space)
}

type Pref = { id: string; visible: boolean }
type Saved = { prefs: Pref[]; widths: Record<string, number> }

export type TableLayout<T> = {
  prefs: Pref[]
  widths: Record<string, number>
  visibleColumns: Col<T>[]
  visibleSet: Set<string>
  columns: Col<T>[]
  setVisible: (id: string, visible: boolean) => void
  move: (fromId: string, toId: string) => void
  moveBy: (id: string, delta: -1 | 1) => void
  setWidth: (id: string, width: number) => void
  reset: () => void
}

const storageKey = (key: string) => `afatura.table.${key}.v1`

function defaults<T>(columns: Col<T>[]): Saved {
  return { prefs: columns.map(c => ({ id: c.id, visible: c.defaultVisible !== false })), widths: {} }
}

function load<T>(key: string, columns: Col<T>[]): Saved {
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey(key)) ?? 'null') as Saved | null
    if (!saved || !Array.isArray(saved.prefs)) return defaults(columns)
    const known = saved.prefs.filter(p => columns.some(c => c.id === p.id))
    const missing = columns.filter(c => !known.some(p => p.id === c.id)).map(c => ({ id: c.id, visible: c.defaultVisible !== false }))
    const prefs = [...known, ...missing].map(p => (columns.find(c => c.id === p.id)?.locked ? { ...p, visible: true } : p))
    return { prefs, widths: saved.widths ?? {} }
  } catch { return defaults(columns) }
}

export function useTableLayout<T>(key: string, columns: Col<T>[]): TableLayout<T> {
  const [state, setState] = useState<Saved>(() => load(key, columns))
  const update = (next: Saved) => {
    setState(next)
    try { localStorage.setItem(storageKey(key), JSON.stringify(next)) } catch { /* storage unavailable */ }
  }
  const visibleColumns = state.prefs.filter(p => p.visible).map(p => columns.find(c => c.id === p.id)).filter((c): c is Col<T> => !!c)
  const visibleSet = useMemo(() => new Set(visibleColumns.map(c => c.id)), [state.prefs]) // eslint-disable-line react-hooks/exhaustive-deps

  return {
    prefs: state.prefs, widths: state.widths, visibleColumns, visibleSet, columns,
    setVisible: (id, visible) => update({ ...state, prefs: state.prefs.map(p => (p.id === id ? { ...p, visible } : p)) }),
    move: (fromId, toId) => {
      if (fromId === toId) return
      const prefs = [...state.prefs]
      const from = prefs.findIndex(p => p.id === fromId)
      const [item] = prefs.splice(from, 1)
      const to = prefs.findIndex(p => p.id === toId)
      prefs.splice(from <= to ? to + 1 : to, 0, item!)
      update({ ...state, prefs })
    },
    moveBy: (id, delta) => {
      const prefs = [...state.prefs]
      const i = prefs.findIndex(p => p.id === id)
      const j = i + delta
      if (j < 0 || j >= prefs.length) return
      ;[prefs[i], prefs[j]] = [prefs[j]!, prefs[i]!]
      update({ ...state, prefs })
    },
    setWidth: (id, width) => update({ ...state, widths: { ...state.widths, [id]: Math.max(60, Math.round(width)) } }),
    reset: () => update(defaults(columns)),
  }
}

export function ColumnPicker<T>({ layout }: { layout: TableLayout<T> }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const close = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [])
  return (
    <div className="col-picker" ref={ref}>
      <button onClick={() => setOpen(!open)} title="Escolher, ordenar e redimensionar colunas">
        <Icon name="settings" size={15} /> Colunas <span className="muted small">{layout.visibleColumns.length}</span>
      </button>
      {open && (
        <div className="popover col-popover">
          <div className="col-head"><b>Colunas</b><button className="link" onClick={layout.reset}>Repor</button></div>
          <p className="small muted col-tip">Também pode arrastar os cabeçalhos para mudar a ordem e o limite direito para mudar a largura.</p>
          {layout.prefs.map((p, idx) => {
            const def = layout.columns.find(c => c.id === p.id)
            if (!def) return null
            return (
              <div key={p.id} className="col-row">
                <label className="inline">
                  <input type="checkbox" checked={p.visible} disabled={def.locked} onChange={() => layout.setVisible(p.id, !p.visible)} />
                  {def.label || '—'}{def.locked && <span className="muted small"> (fixa)</span>}
                </label>
                <span className="col-move">
                  <button className="icon-btn" disabled={idx === 0} onClick={() => layout.moveBy(p.id, -1)} aria-label="Subir">↑</button>
                  <button className="icon-btn" disabled={idx === layout.prefs.length - 1} onClick={() => layout.moveBy(p.id, 1)} aria-label="Descer">↓</button>
                </span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

export function DataTable<T>({
  layout, rows, rowKey, rowClassName, empty, leadingHeader, leadingCell, prefixRows, className,
}: {
  layout: TableLayout<T>
  rows: T[]
  rowKey: (row: T) => string
  rowClassName?: (row: T) => string
  empty?: ReactNode
  leadingHeader?: ReactNode                   // e.g. "select all" checkbox
  leadingCell?: (row: T) => ReactNode         // e.g. row checkbox
  prefixRows?: (colCount: number) => ReactNode
  className?: string
}) {
  const [dragId, setDragId] = useState<string | null>(null)
  const [overId, setOverId] = useState<string | null>(null)
  const cols = layout.visibleColumns
  const hasLeading = leadingCell !== undefined
  const colCount = cols.length + (hasLeading ? 1 : 0)
  const fixedWidth = cols.reduce((s, c) => s + (layout.widths[c.id] ?? c.width ?? 140), 0) + (hasLeading ? 36 : 0)

  function startResize(e: ReactMouseEvent<HTMLSpanElement>, id: string) {
    e.preventDefault(); e.stopPropagation()
    const th = (e.currentTarget as HTMLElement).parentElement!
    const startX = e.clientX
    const startW = th.getBoundingClientRect().width
    const onMove = (ev: MouseEvent) => { th.style.width = `${Math.max(60, startW + ev.clientX - startX)}px` }
    const onUp = (ev: MouseEvent) => {
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
      document.body.classList.remove('resizing')
      layout.setWidth(id, startW + ev.clientX - startX)
    }
    document.body.classList.add('resizing')
    document.addEventListener('mousemove', onMove)
    document.addEventListener('mouseup', onUp)
  }

  return (
    <div className="dt-wrap">
      <table className={`table dt ${className ?? ''}`} style={{ minWidth: fixedWidth }}>
        <colgroup>
          {hasLeading && <col style={{ width: 36 }} />}
          {cols.map(c => <col key={c.id} style={layout.widths[c.id] ?? c.width ? { width: layout.widths[c.id] ?? c.width } : undefined} />)}
        </colgroup>
        <thead>
          <tr>
            {hasLeading && <th className="sel">{leadingHeader}</th>}
            {cols.map(c => (
              <th
                key={c.id}
                className={`${c.className?.includes('num') ? 'num' : ''} ${dragId === c.id ? 'dragging' : ''} ${overId === c.id && dragId !== c.id ? 'drop-target' : ''}`}
                style={layout.widths[c.id] ? { width: layout.widths[c.id] } : undefined}
                draggable
                onDragStart={e => { setDragId(c.id); e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', c.id) }}
                onDragOver={e => { e.preventDefault(); if (overId !== c.id) setOverId(c.id) }}
                onDragLeave={() => setOverId(o => (o === c.id ? null : o))}
                onDrop={e => { e.preventDefault(); const from = dragId ?? e.dataTransfer.getData('text/plain'); if (from) layout.move(from, c.id); setDragId(null); setOverId(null) }}
                onDragEnd={() => { setDragId(null); setOverId(null) }}
                title="Arraste para mudar de posição"
              >
                <span className="th-label">{c.label}</span>
                <span className="col-resize" onMouseDown={e => startResize(e, c.id)} onClick={e => e.stopPropagation()} draggable={false}
                  onDragStart={e => { e.preventDefault(); e.stopPropagation() }} title="Arraste para mudar a largura" />
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {prefixRows?.(colCount)}
          {rows.map(r => (
            <tr key={rowKey(r)} className={rowClassName?.(r) ?? ''}>
              {hasLeading && <td className="sel">{leadingCell!(r)}</td>}
              {cols.map(c => <td key={c.id} className={c.className}>{c.render(r, { visible: layout.visibleSet })}</td>)}
            </tr>
          ))}
          {rows.length === 0 && empty && <tr><td colSpan={colCount} className="center muted">{empty}</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
