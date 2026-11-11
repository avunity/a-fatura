import { useEffect, useState } from 'react'

export type ThemeMode = 'light' | 'dark' | 'system'
const KEY = 'afatura.theme'
const media = () => window.matchMedia('(prefers-color-scheme: dark)')

export function getThemeMode(): ThemeMode {
  const v = localStorage.getItem(KEY)
  return v === 'light' || v === 'dark' ? v : 'system'
}

/** Applies the theme to <html data-theme>. index.html runs the same logic inline before React loads (no flash). */
export function applyTheme(mode: ThemeMode = getThemeMode()) {
  const dark = mode === 'dark' || (mode === 'system' && media().matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? '#111a2b' : '#0f766e')
}

export function useTheme() {
  const [mode, setMode] = useState<ThemeMode>(getThemeMode)
  useEffect(() => {
    applyTheme(mode)
    if (mode !== 'system') return
    const m = media()
    const onChange = () => applyTheme('system')
    m.addEventListener('change', onChange)
    return () => m.removeEventListener('change', onChange)
  }, [mode])
  const set = (m: ThemeMode) => { localStorage.setItem(KEY, m); setMode(m) }
  return { mode, setMode: set }
}
