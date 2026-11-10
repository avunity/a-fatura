const TOKEN_KEY = 'afatura.token'

export const auth = {
  get token() { return localStorage.getItem(TOKEN_KEY) },
  set(token: string) { localStorage.setItem(TOKEN_KEY, token) },
  clear() { localStorage.removeItem(TOKEN_KEY) },
}

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  if (auth.token) headers.Authorization = `Bearer ${auth.token}`
  let payload: BodyInit | undefined
  if (body instanceof FormData) payload = body
  else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body) }

  const res = await fetch(path, { method, headers, body: payload })
  if (res.status === 401 && !path.startsWith('/api/auth/login')) {
    auth.clear()
    if (!location.pathname.startsWith('/login')) location.href = '/login'
  }
  if (!res.ok) {
    let msg = res.statusText
    try { const j = await res.json(); msg = j.title ?? j.message ?? msg } catch { /* empty */ }
    throw new ApiError(res.status, msg)
  }
  if (res.status === 204 || res.status === 202) return undefined as T
  const ct = res.headers.get('content-type') ?? ''
  return (ct.includes('json') ? res.json() : res.text()) as Promise<T>
}

export const api = {
  get: <T>(p: string) => request<T>('GET', p),
  post: <T>(p: string, b?: unknown) => request<T>('POST', p, b ?? {}),
  put: <T>(p: string, b?: unknown) => request<T>('PUT', p, b ?? {}),
  del: <T>(p: string) => request<T>('DELETE', p),
  upload: <T>(p: string, form: FormData) => request<T>('POST', p, form),
  /** Downloads/opens an authenticated file (invoice PDF, ZIP export). */
  async blob(p: string): Promise<Blob> {
    const res = await fetch(p, { headers: { Authorization: `Bearer ${auth.token}` } })
    if (!res.ok) throw new ApiError(res.status, res.statusText)
    return res.blob()
  },
  async download(p: string, fileName: string) {
    const url = URL.createObjectURL(await this.blob(p))
    const a = document.createElement('a')
    a.href = url; a.download = fileName; a.click()
    setTimeout(() => URL.revokeObjectURL(url), 5000)
  },
}

// ── Types ──
export type Membership = { organizationId: string; name: string; nif?: string; type: 'Company' | 'Individual'; role: 'Manager' | 'Collaborator'; mailbox?: string }
export type Client = { organizationId: string; name: string; nif?: string; type: string }
export type Me = { id: string; email: string; name: string; role: 'User' | 'Accountant' | 'Admin'; mustChangePassword: boolean; memberships: Membership[]; clients: Client[] }
export type OrgInfo = {
  id: string; name: string; nif?: string; address?: string; email?: string; type: 'Company' | 'Individual'; submissionDeadlineDay: number
  access: { canSeeAll: boolean; canManage: boolean; isAccountant: boolean; isAdmin: boolean; role?: string }
  accountants: { accountantUserId: string; name: string; email: string }[]
}
export type VatLine = { region?: string; rate: number; base: number; vat: number }
export type Invoice = {
  id: string; status: string; source: string; extractionMethod: string
  supplierNif?: string; supplierName?: string; buyerNif?: string; documentType?: string; documentNumber?: string; atcud?: string
  issueDate?: string; netAmount?: number; vatAmount?: number; totalAmount?: number; currency: string; vatLines: VatLine[]
  summary?: string; licensePlateDetected?: string; fuelLiters?: number; energyKwh?: number
  categoryId?: string; categoryName?: string; categoryColor?: string; vehicleId?: string; vehiclePlate?: string
  classificationConfidence?: number; classificationReason?: string
  collectedInEFatura: boolean; duplicateOfId?: string; hasValidQr: boolean; flags: string[]; processingError?: string
  paidPersonally: boolean; reimbursement: string; notes?: string; tags: string[]
  uploadedByUserId: string; uploadedByName: string; fileName?: string; fileContentType?: string
  createdAt: string; processedAt?: string; reviewedAt?: string
}
export type Category = { id: string; name: string; color: string; keywords: string; isVehicleRelated: boolean; vatDeductiblePercent?: number; isActive: boolean; isGlobal: boolean }
export type Vehicle = {
  id: string; plate: string; make?: string; model?: string; fuelType: string; acquiredAt?: string; acquisitionValue?: number
  insuranceRenewal?: string; inspectionDue?: string; notes?: string; isActive: boolean
  costs: { category: string; total: number; liters: number; kwh: number }[]; yearTotal: number
}
