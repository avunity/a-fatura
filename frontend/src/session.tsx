import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, auth, type Me, type OrgInfo } from './api'

type Session = {
  me: Me | null
  loading: boolean
  reload: () => Promise<void>
  logout: () => void
}

const SessionCtx = createContext<Session>({ me: null, loading: true, reload: async () => {}, logout: () => {} })

export function SessionProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)

  const reload = useCallback(async () => {
    if (!auth.token) { setMe(null); setLoading(false); return }
    try { setMe(await api.get<Me>('/api/auth/me')) } catch { setMe(null) } finally { setLoading(false) }
  }, [])

  useEffect(() => { reload() }, [reload])

  const logout = () => { auth.clear(); setMe(null); location.href = '/login' }
  return <SessionCtx.Provider value={{ me, loading, reload, logout }}>{children}</SessionCtx.Provider>
}

export const useSession = () => useContext(SessionCtx)

/** Loads the organization the user is working on (with the user's effective permissions on it). */
export function useOrg(orgId?: string) {
  const [org, setOrg] = useState<OrgInfo | null>(null)
  const load = useCallback(async () => {
    if (!orgId) return
    setOrg(await api.get<OrgInfo>(`/api/orgs/${orgId}`))
    localStorage.setItem('afatura.org', orgId)
  }, [orgId])
  useEffect(() => { load() }, [load])
  return { org, reload: load }
}

/** Small data-loading hook: const { data, reload } = useLoad(() => api.get(...), [deps]) */
export function useLoad<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const reload = useCallback(async () => {
    setLoading(true)
    try { setData(await fn()); setError(null) } catch (e) { setError((e as Error).message) } finally { setLoading(false) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  useEffect(() => { reload() }, [reload])
  return { data, error, loading, reload, setData }
}
