import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { api, type Category } from '../api'
import { useLoad } from '../session'
import { Card, ErrorBox, Modal, Page, useOrgContext } from '../components/ui'

type Edit = { id?: string; name: string; color: string; keywords: string; isVehicleRelated: boolean; vatDeductiblePercent: string; isActive: boolean }

export default function Categories() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const { data, reload } = useLoad(() => api.get<Category[]>(`/api/orgs/${orgId}/categories`), [orgId])
  const [edit, setEdit] = useState<Edit | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const canEdit = (c?: Category) => (c?.isGlobal ? org?.access.isAdmin : org?.access.canManage || org?.access.isAccountant)

  async function save() {
    if (!edit) return
    const body = { ...edit, vatDeductiblePercent: edit.vatDeductiblePercent === '' ? null : Number(edit.vatDeductiblePercent) }
    try {
      if (edit.id) await api.put(`/api/orgs/${orgId}/categories/${edit.id}`, body)
      else await api.post(`/api/orgs/${orgId}/categories`, body)
      setEdit(null); reload()
    } catch (e) { setErr((e as Error).message) }
  }

  return (
    <Page title="Categorias" actions={canEdit() && <button className="primary" onClick={() => setEdit({ name: '', color: '#0f766e', keywords: '', isVehicleRelated: false, vatDeductiblePercent: '', isActive: true })}>+ Nova categoria</button>}>
      <p className="muted">A classificação automática usa, por esta ordem: a categoria que já escolheu antes para o mesmo fornecedor, a sugestão da IA a partir da imagem e as palavras-chave abaixo.</p>
      <Card>
        <table className="table">
          <thead><tr><th>Categoria</th><th>Palavras-chave</th><th>Viaturas</th><th>% IVA dedutível (indicativo)</th><th></th></tr></thead>
          <tbody>
            {data?.map(c => (
              <tr key={c.id} className={c.isActive ? '' : 'muted'}>
                <td><span className="dot" style={{ background: c.color }} /> {c.name} {c.isGlobal && <span className="muted small">(predefinida)</span>}</td>
                <td className="small muted">{c.keywords}</td>
                <td>{c.isVehicleRelated ? '🚗' : ''}</td>
                <td>{c.vatDeductiblePercent != null ? `${c.vatDeductiblePercent}%` : '—'}</td>
                <td>{canEdit(c) && <button className="link" onClick={() => setEdit({ ...c, vatDeductiblePercent: c.vatDeductiblePercent?.toString() ?? '' })}>Editar</button>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
      {edit && (
        <Modal title={edit.id ? 'Editar categoria' : 'Nova categoria'} onClose={() => setEdit(null)}>
          <ErrorBox error={err} />
          <div className="form-grid">
            <label>Nome<input value={edit.name} onChange={e => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Cor<input type="color" value={edit.color} onChange={e => setEdit({ ...edit, color: e.target.value })} /></label>
            <label className="span2">Palavras-chave (separadas por vírgula)<textarea value={edit.keywords} onChange={e => setEdit({ ...edit, keywords: e.target.value })} /></label>
            <label>% IVA dedutível<input type="number" value={edit.vatDeductiblePercent} onChange={e => setEdit({ ...edit, vatDeductiblePercent: e.target.value })} /></label>
            <label className="inline"><input type="checkbox" checked={edit.isVehicleRelated} onChange={e => setEdit({ ...edit, isVehicleRelated: e.target.checked })} /> Associada a viaturas</label>
            <label className="inline"><input type="checkbox" checked={edit.isActive} onChange={e => setEdit({ ...edit, isActive: e.target.checked })} /> Ativa</label>
          </div>
          <button className="primary" onClick={save}>Guardar</button>
        </Modal>
      )}
    </Page>
  )
}
