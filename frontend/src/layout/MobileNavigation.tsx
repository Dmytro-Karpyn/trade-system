import { BottomNavigation, BottomNavigationAction, Paper } from '@mui/material'
import { Link, useLocation } from 'react-router-dom'
import { hasAnyPermission } from '../auth/contracts'
import type { CurrentUser } from '../auth/contracts'
import { Icon } from '../components/Icon'
import { navigation } from '../routes/navigation'
import { tokens } from '../theme/tokens'
import { mobilePrimaryPaths } from './mobileNavigation'
export function MobileNavigation({ user, more }: { user: CurrentUser; more: () => void }) {
  const { pathname } = useLocation()
  const items = navigation.filter(item => mobilePrimaryPaths.includes(item.path) && hasAnyPermission(user, item.permissions))
  const active = items.find(item => item.path === '/' ? pathname === '/' : pathname === item.path || pathname.startsWith(`${item.path}/`))?.path ?? 'more'
  return <Paper component="nav" aria-label="Мобільна навігація" square sx={{ position: 'fixed', bottom: 0, left: 0, right: 0, zIndex: theme => theme.zIndex.appBar, borderTop: '1px solid', borderColor: 'divider', pb: 'env(safe-area-inset-bottom)' }}><BottomNavigation showLabels value={active} sx={{ height: tokens.mobileNavHeight, bgcolor: 'background.paper' }}>{items.map(item => <BottomNavigationAction key={item.path} value={item.path} component={Link} to={item.path} label={item.title} aria-current={active === item.path ? 'page' : undefined} icon={<Icon name={item.icon} />} sx={{ maxWidth: 'none', px: 0.5, '.MuiBottomNavigationAction-label, .MuiBottomNavigationAction-label.Mui-selected': { fontSize: 12 } }} />)}<BottomNavigationAction value="more" label="Ще" icon={<Icon name="more" />} onClick={more} aria-label="Ще — відкрити решту меню" sx={{ maxWidth: 'none', px: 0.5, '.MuiBottomNavigationAction-label, .MuiBottomNavigationAction-label.Mui-selected': { fontSize: 12 } }} /></BottomNavigation></Paper>
}
