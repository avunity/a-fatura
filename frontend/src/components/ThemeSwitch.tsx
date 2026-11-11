import { useTheme, type ThemeMode } from '../theme'
import { Icon, type IconName } from './icons'

const OPTIONS: { mode: ThemeMode; label: string; icon: IconName }[] = [
  { mode: 'light', label: 'Claro', icon: 'sun' },
  { mode: 'dark', label: 'Escuro', icon: 'moon' },
  { mode: 'system', label: 'Sistema', icon: 'monitor' },
]

export default function ThemeSwitch({ compact = false }: { compact?: boolean }) {
  const { mode, setMode } = useTheme()
  return (
    <div className="theme-switch" role="radiogroup" aria-label="Tema">
      {OPTIONS.map(o => (
        <button key={o.mode} type="button" className={mode === o.mode ? 'on' : ''} onClick={() => setMode(o.mode)} title={o.label} aria-checked={mode === o.mode} role="radio">
          <Icon name={o.icon} size={14} />{!compact && <span>{o.label}</span>}
        </button>
      ))}
    </div>
  )
}
