import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Bars, Card, Page, PeriodPicker, Stat, useOrgContext } from '../components/ui'
import { eur, MONTHS, lastMonth } from '../util'

type Summary = {
  byMonth: { month: number; total: number; vat: number; count: number }[]
  byCategory: { categoryId?: string; name: string; color: string; total: number; count: number }[]
  byVehicle: { vehicleId: string; plate: string; total: number; count: number }[]
  byProject: { projectId: string; name: string; color: string; total: number; count: number }[]
  byStatus: { status: string; count: number }[]
  count: number; collected: number; pendingReimbursement: number
}

export default function Dashboard() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const lm = lastMonth()
  const [year, setYear] = useState(lm.year)
  const [month, setMonth] = useState<number | undefined>(lm.month)
  const { data } = useLoad(() => api.get<Summary>(`/api/orgs/${orgId}/invoices/summary?year=${year}${month ? `&month=${month}` : ''}`), [orgId, year, month])
  const { data: requests } = useLoad(() => api.get<{ id: string; description: string; status: string }[]>(`/api/orgs/${orgId}/requests`), [orgId])

  const total = data?.byCategory.reduce((s, c) => s + c.total, 0) ?? 0
  const review = data?.byStatus.find(s => s.status === 'NeedsReview')?.count ?? 0
  const openReq = requests?.filter(r => r.status === 'Open') ?? []

  return (
    <Page title={org?.name ?? 'Resumo'} actions={<PeriodPicker year={year} month={month} allowAllMonths onChange={(y, m) => { setYear(y); setMonth(m) }} />}>
      {openReq.length > 0 && (
        <div className="alert warn">
          O contabilista pediu {openReq.length} documento(s): {openReq.slice(0, 3).map(r => r.description).join(' · ')}
        </div>
      )}
      <div className="stats">
        <Stat label="Despesas" value={eur(total)} hint={`${data?.count ?? 0} documentos`} />
        <Stat label="Recolhidas no e-Fatura" value={`${data?.collected ?? 0}/${data?.count ?? 0}`} tone={data && data.count > 0 && data.collected < data.count ? 'warn' : 'ok'} />
        <Stat label="Para rever" value={<Link to={`/o/${orgId}/invoices?status=NeedsReview`}>{review}</Link>} tone={review > 0 ? 'warn' : 'ok'} />
        <Stat label="Reembolsos pendentes" value={eur(data?.pendingReimbursement)} />
      </div>
      <div className="grid2">
        <Card title="Por categoria">
          <Bars items={(data?.byCategory ?? []).map(c => ({ label: c.name, value: c.total, color: c.color, display: eur(c.total) }))} />
        </Card>
        <Card title={`Evolução ${year}`}>
          <Bars items={MONTHS.map((m, i) => {
            const v = data?.byMonth.find(x => x.month === i + 1)?.total ?? 0
            return { label: m.slice(0, 3), value: v, display: eur(v), color: i + 1 === month ? 'var(--accent)' : 'var(--accent-soft)' }
          })} />
        </Card>
        {(data?.byProject?.length ?? 0) > 0 && (
          <Card title="Por projeto" right={<Link to={`/o/${orgId}/projects`} className="small">ver projetos</Link>}>
            <Bars items={data!.byProject.map(p => ({ label: p.name, value: p.total, color: p.color, display: eur(p.total) }))} />
          </Card>
        )}
        {(data?.byVehicle.length ?? 0) > 0 && (
          <Card title="Por viatura">
            <Bars items={data!.byVehicle.map(v => ({ label: v.plate, value: v.total, display: eur(v.total) }))} />
          </Card>
        )}
      </div>
    </Page>
  )
}
