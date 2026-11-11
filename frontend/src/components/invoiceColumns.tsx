import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { Invoice } from '../api'
import { StatusPill } from './ui'
import { date, dateTime, eur, SOURCE_LABEL } from '../util'

export type ColumnId =
  | 'date' | 'supplier' | 'nif' | 'document' | 'atcud' | 'category' | 'project' | 'vehicle'
  | 'net' | 'vat' | 'total' | 'efatura' | 'status' | 'uploadedBy' | 'source' | 'createdAt' | 'reimbursement' | 'notes'

export type ColumnDef = {
  id: ColumnId
  label: string
  defaultVisible: boolean
  locked?: boolean          // always visible (identifies the row)
  className?: string
  render: (i: Invoice, ctx: { orgId: string; visible: Set<ColumnId> }) => ReactNode
}

const REIMB: Record<string, string> = { Pending: 'Pendente', Approved: 'Aprovado', Paid: 'Pago', Rejected: 'Rejeitado' }

export const COLUMNS: ColumnDef[] = [
  { id: 'date', label: 'Data', defaultVisible: true, render: i => date(i.issueDate) },
  {
    id: 'supplier', label: 'Fornecedor', defaultVisible: true, locked: true,
    render: (i, { orgId, visible }) => {
      const sub = [!visible.has('nif') && i.supplierNif, !visible.has('uploadedBy') && i.uploadedByName].filter(Boolean).join(' · ')
      return <>
        <Link to={`/o/${orgId}/invoices/${i.id}`}>{i.supplierName ?? i.fileName ?? '—'}</Link>
        {(i.status === 'Pending' || i.status === 'Processing') && !i.supplierName && <div className="muted small">a ler o documento…</div>}
        {sub && <div className="muted small">{sub}</div>}
      </>
    },
  },
  { id: 'nif', label: 'NIF', defaultVisible: false, className: 'small', render: i => i.supplierNif ?? '—' },
  {
    id: 'document', label: 'Documento', defaultVisible: true, className: 'small',
    render: i => <>{i.documentNumber ?? '—'}{i.duplicateOfId && <span className="pill s-Failed">duplicado</span>}</>,
  },
  { id: 'atcud', label: 'ATCUD', defaultVisible: false, className: 'small', render: i => i.atcud ?? '—' },
  {
    id: 'category', label: 'Categoria', defaultVisible: true,
    render: i => i.categoryName ? <span className="tag" style={{ borderColor: i.categoryColor }}>{i.categoryName}</span> : <span className="muted">—</span>,
  },
  { id: 'project', label: 'Projeto', defaultVisible: true, render: i => i.projectName ? <span className="tag" style={{ borderColor: i.projectColor }}>{i.projectName}</span> : '' },
  { id: 'vehicle', label: 'Viatura', defaultVisible: true, render: i => i.vehiclePlate ?? '' },
  { id: 'net', label: 'Base', defaultVisible: false, className: 'num', render: i => eur(i.netAmount) },
  { id: 'vat', label: 'IVA', defaultVisible: true, className: 'num', render: i => eur(i.vatAmount) },
  { id: 'total', label: 'Total', defaultVisible: true, className: 'num', render: i => <b>{eur(i.totalAmount)}</b> },
  { id: 'efatura', label: 'e-Fatura', defaultVisible: true, render: i => i.collectedInEFatura ? <span className="ok">✓ recolhida</span> : <span className="muted">—</span> },
  { id: 'status', label: 'Estado', defaultVisible: true, render: i => <><StatusPill status={i.status} />{i.flags.length > 0 && <span title={i.flags.join('\n')}> ⚠️</span>}</> },
  { id: 'uploadedBy', label: 'Carregada por', defaultVisible: false, className: 'small', render: i => i.uploadedByName },
  { id: 'source', label: 'Origem', defaultVisible: true, className: 'small muted', render: i => SOURCE_LABEL[i.source] ?? i.source },
  { id: 'createdAt', label: 'Carregada em', defaultVisible: false, className: 'small', render: i => dateTime(i.createdAt) },
  { id: 'reimbursement', label: 'Reembolso', defaultVisible: false, className: 'small', render: i => i.paidPersonally ? (REIMB[i.reimbursement] ?? i.reimbursement) : '' },
  { id: 'notes', label: 'Notas', defaultVisible: false, className: 'small muted', render: i => i.notes ?? '' },
]

export type ColumnPref = { id: ColumnId; visible: boolean }
const KEY = 'afatura.invoiceColumns.v1'

export const defaultPrefs = (): ColumnPref[] => COLUMNS.map(c => ({ id: c.id, visible: c.defaultVisible }))

/** Saved order/visibility merged with the current column set (new columns are appended, removed ones dropped). */
export function loadPrefs(): ColumnPref[] {
  try {
    const saved = JSON.parse(localStorage.getItem(KEY) ?? 'null') as ColumnPref[] | null
    if (!Array.isArray(saved)) return defaultPrefs()
    const known = saved.filter(p => COLUMNS.some(c => c.id === p.id))
    const missing = COLUMNS.filter(c => !known.some(p => p.id === c.id)).map(c => ({ id: c.id, visible: c.defaultVisible }))
    return [...known, ...missing].map(p => (COLUMNS.find(c => c.id === p.id)?.locked ? { ...p, visible: true } : p))
  } catch { return defaultPrefs() }
}

export function savePrefs(prefs: ColumnPref[]) {
  try { localStorage.setItem(KEY, JSON.stringify(prefs)) } catch { /* storage unavailable */ }
}
