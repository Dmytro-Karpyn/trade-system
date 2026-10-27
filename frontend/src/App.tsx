import { useEffect } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useAppServices } from './app/context'
import { AppRoutes } from './routes/AppRoutes'
import { sectionTitle } from './routes/navigation'

export default function App() {
  const { api } = useAppServices()
  const navigate = useNavigate()
  const location = useLocation()
  useEffect(() => {
    api.setForbiddenHandler(() => { void navigate('/forbidden', { replace: true }) })
    return () => { api.setForbiddenHandler() }
  }, [api, navigate])
  useEffect(() => {
    const title = location.pathname === '/login' ? 'Вхід' : sectionTitle(location.pathname) ?? 'Робочий простір'
    document.title = `${title} · Trade System`
  }, [location.pathname])
  return <AppRoutes />
}
