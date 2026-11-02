import { useState } from 'react'
import { Box, useMediaQuery, useTheme } from '@mui/material'
import { Outlet, useLocation } from 'react-router-dom'
import { useCurrentUser } from '../auth/queries'
import { sectionTitle } from '../routes/navigation'
import { tokens } from '../theme/tokens'
import { Header } from './Header'
import { Sidebar } from './Sidebar'
import { MobileNavigation } from './MobileNavigation.tsx'

export function AppLayout() {
  const user = useCurrentUser().data
  const desktop = useMediaQuery(useTheme().breakpoints.up('lg'))
  const [drawer, setDrawer] = useState<'all' | 'more' | null>(null)
  const [collapsed, setCollapsed] = useState(false)
  const { pathname } = useLocation()
  const title = sectionTitle(pathname) ?? (pathname === '/forbidden' ? 'Доступ обмежено' : 'Сторінку не знайдено')
  const width = collapsed ? tokens.collapsedSidebarWidth : tokens.sidebarWidth
  if (!user) return null
  return <Box sx={{ minHeight: '100dvh' }}>
    <Box component="a" href="#main-content" sx={{ position: 'fixed', zIndex: 2000, top: -80, left: 16, p: 1.5, bgcolor: 'background.paper', borderRadius: 1, '&:focus': { top: 8 } }}>Перейти до вмісту</Box>
    <Sidebar user={user} desktop={desktop} open={drawer !== null} moreOnly={drawer === 'more'} collapsed={collapsed} close={() => setDrawer(null)} />
    <Header user={user} title={title} desktop={desktop} sidebarWidth={width} collapsed={collapsed} toggleSidebar={() => setCollapsed(value => !value)} openMenu={() => setDrawer('all')} />
    <Box component="main" id="main-content" tabIndex={-1} sx={desktop ? { ml: `${width}px`, pt: `${tokens.headerHeight}px`, minWidth: 0 } : { mt: `${tokens.headerHeight}px`, height: `calc(100dvh - ${tokens.headerHeight + tokens.mobileNavHeight}px - env(safe-area-inset-bottom))`, overflowY: 'auto', minWidth: 0 }}>
      <Box sx={{ p: { xs: 2, md: 2.5 }, mx: 'auto', maxWidth: tokens.contentMaxWidth }}><Outlet /></Box>
    </Box>
    {!desktop && <MobileNavigation user={user} more={() => setDrawer('more')} />}
  </Box>
}
