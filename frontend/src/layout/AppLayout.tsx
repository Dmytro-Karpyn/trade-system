import { useState } from 'react'
import { Box, useMediaQuery, useTheme } from '@mui/material'
import { Outlet, useLocation } from 'react-router-dom'
import { useCurrentUser } from '../auth/queries'
import { sectionTitle } from '../routes/navigation'
import { tokens } from '../theme/tokens'
import { Header } from './Header'
import { Sidebar } from './Sidebar'

export function AppLayout() {
  const user = useCurrentUser().data
  const desktop = useMediaQuery(useTheme().breakpoints.up('lg'))
  const [drawerOpen, setDrawerOpen] = useState(false)
  const location = useLocation()
  const title = sectionTitle(location.pathname) ?? (location.pathname === '/forbidden' ? 'Доступ обмежено' : 'Сторінку не знайдено')
  if (!user) return null
  return <Box sx={{ minHeight: '100dvh' }}>
    <Box component="a" href="#main-content" sx={{ position: 'fixed', zIndex: 2000, top: -80, left: 16, p: 1.5, bgcolor: '#fff', borderRadius: 1, '&:focus': { top: 8 } }}>Перейти до вмісту</Box>
    <Sidebar user={user} desktop={desktop} open={drawerOpen} close={() => setDrawerOpen(false)} />
    <Header user={user} title={title} desktop={desktop} openMenu={() => setDrawerOpen(true)} />
    <Box component="main" id="main-content" tabIndex={-1} sx={{ ml: desktop ? `${tokens.sidebarWidth}px` : 0, pt: `${tokens.headerHeight}px`, minWidth: 0 }}>
      <Box sx={{ p: { xs: 2.5, md: 4 }, mx: 'auto', maxWidth: tokens.contentMaxWidth }}><Outlet /></Box>
    </Box>
  </Box>
}
