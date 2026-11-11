import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { SessionProvider } from './session'
import './styles.css'
import { applyTheme } from './theme'

applyTheme()
// follow OS changes live when the mode is "system"
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => applyTheme())

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <SessionProvider>
        <App />
      </SessionProvider>
    </BrowserRouter>
  </StrictMode>,
)
