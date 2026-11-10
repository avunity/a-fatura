import { Navigate, Route, Routes } from 'react-router-dom'
import { useSession } from './session'
import Layout from './components/Layout'
import Login from './pages/Login'
import Register from './pages/Register'
import Dashboard from './pages/Dashboard'
import Invoices from './pages/Invoices'
import InvoiceDetail from './pages/InvoiceDetail'
import Vehicles from './pages/Vehicles'
import Categories from './pages/Categories'
import Banks from './pages/Banks'
import EFatura from './pages/EFatura'
import Team from './pages/Team'
import Messages from './pages/Messages'
import Settings from './pages/Settings'
import Accountant from './pages/Accountant'
import Backoffice from './pages/Backoffice'
import Account from './pages/Account'
import Projects from './pages/Projects'

function Home() {
  const { me } = useSession()
  if (!me) return <Navigate to="/login" replace />
  if (me.role === 'Admin') return <Navigate to="/backoffice" replace />
  if (me.role === 'Accountant') return <Navigate to="/accountant" replace />
  const saved = localStorage.getItem('afatura.org')
  const org = me.memberships.find(m => m.organizationId === saved) ?? me.memberships[0]
  return org ? <Navigate to={`/o/${org.organizationId}`} replace /> : <div className="center">Sem entidades associadas.</div>
}

export default function App() {
  const { me, loading } = useSession()
  if (loading) return <div className="center">A carregar…</div>

  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
      {me ? (
        <Route element={<Layout />}>
          <Route path="/" element={<Home />} />
          <Route path="/accountant" element={<Accountant />} />
          <Route path="/backoffice" element={<Backoffice />} />
          <Route path="/account" element={<Account />} />
          <Route path="/o/:orgId" element={<Dashboard />} />
          <Route path="/o/:orgId/invoices" element={<Invoices />} />
          <Route path="/o/:orgId/invoices/:id" element={<InvoiceDetail />} />
          <Route path="/o/:orgId/vehicles" element={<Vehicles />} />
          <Route path="/o/:orgId/projects" element={<Projects />} />
          <Route path="/o/:orgId/categories" element={<Categories />} />
          <Route path="/o/:orgId/banks" element={<Banks />} />
          <Route path="/o/:orgId/efatura" element={<EFatura />} />
          <Route path="/o/:orgId/team" element={<Team />} />
          <Route path="/o/:orgId/messages" element={<Messages />} />
          <Route path="/o/:orgId/settings" element={<Settings />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Route>
      ) : (
        <Route path="*" element={<Navigate to="/login" replace />} />
      )}
    </Routes>
  )
}
