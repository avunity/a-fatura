import { Link } from 'react-router-dom'
import type { Invoice } from '../api'
import { StatusPill } from './ui'
import { date, dateTime, eur, SOURCE_LABEL } from '../util'

import type { Col } from './DataTable'

const REIMB: Record<string, string> = { Pending: 'Pendente', Approved: 'Aprovado', Paid: 'Pago', Rejected: 'Rejeitado' }

export const invoiceColumns = (orgId: string): Col<Invoice>[] => [
  { id: 'date', label: 'Data', width: 100, render: i => date(i.issueDate) },
  {
    id: 'supplier', label: 'Fornecedor', width: 240, defaultVisible: true, locked: true,
    render: (i, { visible }) => {
      const sub = [!visible.has('nif') && i.supplierNif, !visible.has('uploadedBy') && i.uploadedByName].filter(Boolean).join(' · ')
      return <>
        <Link to={`/o/${orgId}/invoices/${i.id}`}>{i.supplierName ?? i.fileName ?? '—'}</Link>
        {(i.status === 'Pending' || i.status === 'Processing') && !i.supplierName && <div className="muted small">a ler o documento…</div>}
        {sub && <div className="muted small">{sub}</div>}
      </>
    },
  },
  { id: 'nif', label: 'NIF', width: 110, defaultVisible: false, className: 'small', render: i => i.supplierNif ?? '—' },
  {
    id: 'document', label: 'Documento', width: 150, defaultVisible: true, className: 'small',
    render: i => <>{i.documentNumber ?? '—'}{i.duplicateOfId && <span className="pill s-Failed">duplicado</span>}</>,
  },
  { id: 'atcud', label: 'ATCUD', width: 150, defaultVisible: false, className: 'small', render: i => i.atcud ?? '—' },
  {
    id: 'category', label: 'Categoria', width: 170, defaultVisible: true,
    render: i => i.categoryName ? <span className="tag" style={{ borderColor: i.categoryColor }}>{i.categoryName}</span> : <span className="muted">—</span>,
  },
  { id: 'project', label: 'Projeto', width: 140, defaultVisible: true, render: i => i.projectName ? <span className="tag" style={{ borderColor: i.projectColor }}>{i.projectName}</span> : '' },
  { id: 'vehicle', label: 'Viatura', width: 100, defaultVisible: true, render: i => i.vehiclePlate ?? '' },
  { id: 'net', label: 'Base', width: 100, defaultVisible: false, className: 'num', render: i => eur(i.netAmount) },
  { id: 'vat', label: 'IVA', width: 90, defaultVisible: true, className: 'num', render: i => eur(i.vatAmount) },
  { id: 'total', label: 'Total', width: 110, defaultVisible: true, className: 'num', render: i => <b>{eur(i.totalAmount)}</b> },
  { id: 'efatura', label: 'e-Fatura', width: 120, defaultVisible: true, render: i => i.collectedInEFatura ? <span className="ok">✓ recolhida</span> : <span className="muted">—</span> },
  { id: 'status', label: 'Estado', width: 140, defaultVisible: true, render: i => <><StatusPill status={i.status} />{i.flags.length > 0 && <span title={i.flags.join('\n')}> ⚠️</span>}</> },
  { id: 'uploadedBy', label: 'Carregada por', width: 140, defaultVisible: false, className: 'small', render: i => i.uploadedByName },
  { id: 'source', label: 'Origem', width: 80, defaultVisible: true, className: 'small muted', render: i => SOURCE_LABEL[i.source] ?? i.source },
  { id: 'createdAt', label: 'Carregada em', width: 130, defaultVisible: false, className: 'small', render: i => dateTime(i.createdAt) },
  { id: 'reimbursement', label: 'Reembolso', width: 110, defaultVisible: false, className: 'small', render: i => i.paidPersonally ? (REIMB[i.reimbursement] ?? i.reimbursement) : '' },
  { id: 'notes', label: 'Notas', width: 200, defaultVisible: false, className: 'small muted', render: i => i.notes ?? '' },
]

