import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { useLoad } from '../session'
import { Card, Dropzone, ErrorBox, Modal, Page, PeriodPicker, useOrgContext } from '../components/ui'
import { date, dateTime, eur, lastMonth } from '../util'

type Import = { id: string; fileName: string; periodStart: string; periodEnd: string; transactionsTotal: number; transactionsNew: number; transactionsDuplicate: number; overlapWarning?: string; createdAt: string }
type Account = {
  id: string; bankName: string; alias: string; iban?: string; currency: string
  imports: Import[]; coverage: { start: string; end: string }[]
  stats?: { count: number; matched: number; last: string }
}
type Tx = { id: string; date: string; description: string; amount: number; balance?: number; matchedInvoiceId?: string }
type Suggestion = { transaction: Tx; candidates: { id: string; supplierName?: string; documentNumber?: string; issueDate: string; totalAmount: number }[] }

export default function Banks() {
  const { orgId } = useParams()
  const { org } = useOrgContext()
  const { data, reload } = useLoad(() => api.get<Account[]>(`/api/orgs/${orgId}/bank-accounts`), [orgId])
  const [selected, setSelected] = useState<string | null>(null)
  const [create, setCreate] = useState<{ bankName: string; alias: string; iban: string } | null>(null)
  const [result, setResult] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const lm = lastMonth()
  const [period, setPeriod] = useState(lm)

  const account = data?.find(a => a.id === selected) ?? data?.[0]
  const { data: txs, reload: reloadTx } = useLoad(
    () => account ? api.get<Tx[]>(`/api/orgs/${orgId}/bank-accounts/${account.id}/transactions?year=${period.year}&month=${period.month}`) : Promise.resolve([]),
    [orgId, account?.id, period.year, period.month])
  const { data: suggestions, reload: reloadSug } = useLoad(
    () => account ? api.get<Suggestion[]>(`/api/orgs/${orgId}/bank-accounts/${account.id}/suggestions?year=${period.year}&month=${period.month}`) : Promise.resolve([]),
    [orgId, account?.id, period.year, period.month])

  async function upload(files: File[]) {
    if (!account || files.length === 0) return
    setErr(null); setResult(null)
    const form = new FormData()
    form.append('file', files[0])
    try {
      const r = await api.upload<{ import: Import }>(`/api/orgs/${orgId}/bank-accounts/${account.id}/statements`, form)
      setResult(`${r.import.transactionsNew} movimentos novos, ${r.import.transactionsDuplicate} já existentes (ignorados). ${r.import.overlapWarning ?? ''}`)
      reload(); reloadTx(); reloadSug()
    } catch (e) { setErr((e as Error).message) }
  }

  async function match(txId: string, invoiceId: string | null) {
    await api.post(`/api/orgs/${orgId}/bank-accounts/${account!.id}/transactions/${txId}/match`, { invoiceId })
    reloadTx(); reloadSug()
  }

  return (
    <Page title="Contas bancárias" actions={org?.access.canManage && <button className="primary" onClick={() => setCreate({ bankName: '', alias: '', iban: '' })}>+ Nova conta</button>}>
      <p className="muted">Sem ligação ao banco: carregue os extratos em CSV. O sistema regista os períodos carregados por conta e ignora movimentos repetidos quando os extratos se sobrepõem.</p>
      <div className="tabs">
        {data?.map(a => <button key={a.id} className={account?.id === a.id ? 'on' : ''} onClick={() => setSelected(a.id)}>{a.alias}<span className="muted small"> {a.bankName}</span></button>)}
      </div>

      {account && (
        <>
          <div className="grid2">
            <Card title="Carregar extrato">
              <Dropzone label="Extrato CSV" accept=".csv,text/csv" multiple={false} onFiles={upload} />
              <ErrorBox error={err} />
              {result && <div className="alert ok">{result}</div>}
            </Card>
            <Card title="Períodos carregados">
              <div className="timeline">
                {account.coverage.map((c, i) => <span key={i} className="tag">{date(c.start)} → {date(c.end)}</span>)}
                {account.coverage.length === 0 && <span className="muted">Nenhum extrato carregado.</span>}
              </div>
              <table className="table small">
                <thead><tr><th>Ficheiro</th><th>Período</th><th>Novos</th><th>Repetidos</th><th>Carregado</th></tr></thead>
                <tbody>{account.imports.map(i => (
                  <tr key={i.id} title={i.overlapWarning}>
                    <td>{i.fileName} {i.overlapWarning && '⚠️'}</td><td>{date(i.periodStart)} – {date(i.periodEnd)}</td>
                    <td>{i.transactionsNew}</td><td>{i.transactionsDuplicate}</td><td>{dateTime(i.createdAt)}</td>
                  </tr>))}
                </tbody>
              </table>
            </Card>
          </div>

          {(suggestions?.length ?? 0) > 0 && (
            <Card title="Sugestões de conciliação (movimento ↔ fatura com o mesmo valor)">
              <table className="table small">
                <tbody>{suggestions!.map(s => (
                  <tr key={s.transaction.id}>
                    <td>{date(s.transaction.date)}</td><td>{s.transaction.description}</td><td className="num">{eur(s.transaction.amount)}</td>
                    <td>{s.candidates.map(c => <button key={c.id} onClick={() => match(s.transaction.id, c.id)}>↔ {c.supplierName ?? c.documentNumber} ({date(c.issueDate)})</button>)}</td>
                  </tr>))}
                </tbody>
              </table>
            </Card>
          )}

          <Card title="Movimentos" right={<PeriodPicker year={period.year} month={period.month} onChange={(y, m) => setPeriod({ year: y, month: m ?? 1 })} />}>
            <table className="table">
              <thead><tr><th>Data</th><th>Descrição</th><th className="num">Montante</th><th className="num">Saldo</th><th>Fatura</th></tr></thead>
              <tbody>{txs?.map(t => (
                <tr key={t.id}>
                  <td>{date(t.date)}</td><td>{t.description}</td>
                  <td className={`num ${t.amount < 0 ? 'bad-text' : 'ok'}`}>{eur(t.amount)}</td><td className="num muted">{eur(t.balance)}</td>
                  <td>{t.matchedInvoiceId ? <><a href={`/o/${orgId}/invoices/${t.matchedInvoiceId}`}>ver</a> <button className="link" onClick={() => match(t.id, null)}>desligar</button></> : ''}</td>
                </tr>))}
              </tbody>
            </table>
          </Card>
        </>
      )}

      {create && (
        <Modal title="Nova conta bancária" onClose={() => setCreate(null)}>
          <div className="form-grid">
            <label>Banco<input value={create.bankName} onChange={e => setCreate({ ...create, bankName: e.target.value })} placeholder="CGD, Millennium, Revolut…" /></label>
            <label>Nome da conta<input value={create.alias} onChange={e => setCreate({ ...create, alias: e.target.value })} placeholder="Conta à ordem" /></label>
            <label className="span2">IBAN (opcional)<input value={create.iban} onChange={e => setCreate({ ...create, iban: e.target.value })} /></label>
          </div>
          <button className="primary" onClick={async () => { await api.post(`/api/orgs/${orgId}/bank-accounts`, create); setCreate(null); reload() }}>Criar</button>
        </Modal>
      )}
    </Page>
  )
}
