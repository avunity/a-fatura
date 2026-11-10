import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, type Vehicle } from '../api'
import { useLoad } from '../session'
import { Bars, Card, ErrorBox, Modal, Page, useOrgContext } from '../components/ui'
import { date, eur, FUEL_LABEL } from '../util'

const empty = { plate: '', make: '', model: '', fuelType: 'Diesel', acquiredAt: '', acquisitionValue: '', insuranceRenewal: '', inspectionDue: '', notes: '', isActive: true }

export default function Vehicles() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const [year, setYear] = useState(new Date().getFullYear())
  const { data, reload } = useLoad(() => api.get<Vehicle[]>(`/api/orgs/${orgId}/vehicles?year=${year}`), [orgId, year])
  const [edit, setEdit] = useState<(typeof empty & { id?: string }) | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const canManage = org?.access.canManage

  async function save() {
    if (!edit) return
    setErr(null)
    const body = {
      ...edit,
      acquiredAt: edit.acquiredAt || null, insuranceRenewal: edit.insuranceRenewal || null, inspectionDue: edit.inspectionDue || null,
      acquisitionValue: edit.acquisitionValue === '' ? null : Number(edit.acquisitionValue),
    }
    try {
      if (edit.id) await api.put(`/api/orgs/${orgId}/vehicles/${edit.id}`, body)
      else await api.post(`/api/orgs/${orgId}/vehicles`, body)
      setEdit(null); reload()
    } catch (e) { setErr((e as Error).message) }
  }

  const soon = (d?: string) => d && (new Date(d).getTime() - Date.now()) / 86400000 < 30

  return (
    <Page title="Viaturas" actions={<>
      <select value={year} onChange={e => setYear(Number(e.target.value))}>{[0, 1, 2, 3].map(i => <option key={i}>{new Date().getFullYear() - i}</option>)}</select>
      {canManage && <button className="primary" onClick={() => setEdit({ ...empty })}>+ Nova viatura</button>}
    </>}>
      <p className="muted">As faturas de combustível, carregamentos, seguros, portagens e manutenção são associadas automaticamente pela matrícula impressa no documento, pelo histórico do fornecedor, ou quando só existe uma viatura compatível.</p>
      <div className="grid2">
        {data?.map(v => (
          <Card key={v.id} title={`${v.plate} ${v.make ?? ''} ${v.model ?? ''}`} right={canManage && <button className="link" onClick={() => setEdit({
            ...empty, ...v, make: v.make ?? '', model: v.model ?? '', notes: v.notes ?? '', acquiredAt: v.acquiredAt ?? '',
            insuranceRenewal: v.insuranceRenewal ?? '', inspectionDue: v.inspectionDue ?? '', acquisitionValue: v.acquisitionValue?.toString() ?? '',
          })}>Editar</button>}>
            <div className="row gap small muted">
              <span>{FUEL_LABEL[v.fuelType]}</span>
              {!v.isActive && <span className="pill s-Failed">inativa</span>}
              {v.insuranceRenewal && <span className={soon(v.insuranceRenewal) ? 'warn-text' : ''}>Seguro: {date(v.insuranceRenewal)}</span>}
              {v.inspectionDue && <span className={soon(v.inspectionDue) ? 'warn-text' : ''}>Inspeção: {date(v.inspectionDue)}</span>}
            </div>
            <h2>{eur(v.yearTotal)} <span className="muted small">em {year}</span></h2>
            <Bars items={v.costs.map(c => ({ label: c.category, value: c.total, display: eur(c.total) }))} />
            {v.costs.some(c => c.liters > 0) && <p className="small muted">{v.costs.reduce((s, c) => s + c.liters, 0).toFixed(1)} litros</p>}
            {v.costs.some(c => c.kwh > 0) && <p className="small muted">{v.costs.reduce((s, c) => s + c.kwh, 0).toFixed(1)} kWh</p>}
          </Card>
        ))}
        {data?.length === 0 && <div className="muted">Nenhuma viatura registada.</div>}
      </div>

      {edit && (
        <Modal title={edit.id ? 'Editar viatura' : 'Nova viatura'} onClose={() => setEdit(null)}>
          <ErrorBox error={err} />
          <div className="form-grid">
            <label>Matrícula<input value={edit.plate} onChange={e => setEdit({ ...edit, plate: e.target.value })} placeholder="AA-00-BB" /></label>
            <label>Combustível
              <select value={edit.fuelType} onChange={e => setEdit({ ...edit, fuelType: e.target.value })}>
                {Object.entries(FUEL_LABEL).map(([k, l]) => <option key={k} value={k}>{l}</option>)}
              </select>
            </label>
            <label>Marca<input value={edit.make} onChange={e => setEdit({ ...edit, make: e.target.value })} /></label>
            <label>Modelo<input value={edit.model} onChange={e => setEdit({ ...edit, model: e.target.value })} /></label>
            <label>Data de aquisição<input type="date" value={edit.acquiredAt} onChange={e => setEdit({ ...edit, acquiredAt: e.target.value })} /></label>
            <label>Valor de aquisição<input type="number" value={edit.acquisitionValue} onChange={e => setEdit({ ...edit, acquisitionValue: e.target.value })} /></label>
            <label>Renovação do seguro<input type="date" value={edit.insuranceRenewal} onChange={e => setEdit({ ...edit, insuranceRenewal: e.target.value })} /></label>
            <label>Próxima inspeção<input type="date" value={edit.inspectionDue} onChange={e => setEdit({ ...edit, inspectionDue: e.target.value })} /></label>
            <label className="span2">Notas<textarea value={edit.notes} onChange={e => setEdit({ ...edit, notes: e.target.value })} /></label>
            <label className="inline"><input type="checkbox" checked={edit.isActive} onChange={e => setEdit({ ...edit, isActive: e.target.checked })} /> Ativa</label>
          </div>
          <button className="primary" onClick={save}>Guardar</button>
        </Modal>
      )}
    </Page>
  )
}
