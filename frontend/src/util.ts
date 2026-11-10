export const eur = (v?: number | null) =>
  v == null ? '—' : new Intl.NumberFormat('pt-PT', { style: 'currency', currency: 'EUR' }).format(v)

export const date = (v?: string | null) => (v ? new Date(v).toLocaleDateString('pt-PT') : '—')
export const dateTime = (v?: string | null) => (v ? new Date(v).toLocaleString('pt-PT', { dateStyle: 'short', timeStyle: 'short' }) : '—')
export const pct = (v?: number | null) => (v == null ? '—' : `${Math.round(v * 100)}%`)

export const MONTHS = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']

export const STATUS_LABEL: Record<string, string> = {
  Pending: 'Em fila', Processing: 'A processar', Processed: 'Processada', NeedsReview: 'Rever', Failed: 'Erro',
}
export const FUEL_LABEL: Record<string, string> = {
  Gasoline: 'Gasolina', Diesel: 'Gasóleo', Electric: 'Elétrico', PlugInHybrid: 'Híbrido plug-in', Hybrid: 'Híbrido', Lpg: 'GPL', Other: 'Outro',
}
export const SOURCE_LABEL: Record<string, string> = { Upload: 'Web', Mobile: 'App', Email: 'Email', Manual: 'Manual' }

export function lastMonth(): { year: number; month: number } {
  const d = new Date()
  d.setDate(1)
  d.setMonth(d.getMonth() - 1)
  return { year: d.getFullYear(), month: d.getMonth() + 1 }
}
