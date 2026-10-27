import { Box, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Stack, Typography } from '@mui/material'
import { NavLink } from 'react-router-dom'
import { hasAnyPermission } from '../auth/contracts'
import type { CurrentUser } from '../auth/contracts'
import { Brand } from '../components/Brand'
import { Icon } from '../components/Icon'
import { navigation } from '../routes/navigation'
import { tokens } from '../theme/tokens'

export function Sidebar({ user, desktop, open, close }: { user: CurrentUser; desktop: boolean; open: boolean; close: () => void }) {
  const allowed = navigation.filter(item => hasAnyPermission(user, item.permissions))
  return (
    <Drawer variant={desktop ? 'permanent' : 'temporary'} open={desktop || open} onClose={close}
      slotProps={{ paper: { sx: { width: desktop ? tokens.sidebarWidth : tokens.mobileSidebarWidth, maxWidth: 'calc(100vw - 32px)', bgcolor: tokens.colors.sidebar, color: '#fff', border: 0 } } }}>
      <Box component="nav" aria-label="Основна навігація" sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', px: 3, minHeight: tokens.headerHeight, flexShrink: 0 }}>
          <Brand light />
          {!desktop && <IconButton aria-label="Закрити меню" onClick={close} sx={{ color: tokens.colors.sidebarText, mr: -1 }}><Icon name="close" /></IconButton>}
        </Stack>
        <Box sx={{ mx: 2, my: 2, px: 1.75, py: 1.5, border: '1px solid #2b3850', borderRadius: 1, display: 'flex', gap: 1.5, alignItems: 'center' }}>
          <Box sx={{ width: 30, height: 30, display: 'grid', placeItems: 'center', bgcolor: '#25334b', borderRadius: 0.75 }}><Icon name="stock" sx={{ fontSize: 18, color: '#a9bce1' }} /></Box>
          <Box sx={{ minWidth: 0 }}><Typography sx={{ fontSize: 12, fontWeight: 600 }}>Робочий простір</Typography><Typography title={user.organizationId} sx={{ fontSize: 10, color: '#96a8c4' }}>Організація · {user.organizationId.slice(0, 8)}</Typography></Box>
        </Box>
        <Box sx={{ px: 1.5, pb: 2, flex: 1, overflowY: 'auto' }}>
          {(['Робочий простір', 'Операції', 'Управління'] as const).map(group => {
            const items = allowed.filter(item => item.group === group)
            if (!items.length) return null
            return <Box key={group} sx={{ mb: 1.75 }}>
              <Typography variant="overline" sx={{ color: '#71839f', px: 1.5, display: 'block', my: 1 }}>{group}</Typography>
              <List disablePadding>
                {items.map(item => <ListItemButton key={item.path} component={NavLink} to={item.path} end={item.path === '/'} onClick={desktop ? undefined : close}
                  sx={{ minHeight: 41, borderRadius: 0.75, mb: 0.5, px: 1.5, color: tokens.colors.sidebarText, '&:hover': { bgcolor: '#1b2d49', color: '#fff' }, '&.active': { bgcolor: tokens.colors.sidebarActive, color: '#fff', '&::before': { content: '""', position: 'absolute', left: -12, width: 3, height: 22, borderRadius: 1, bgcolor: '#6094ff' } } }}>
                  <ListItemIcon sx={{ minWidth: 33, color: 'inherit' }}><Icon name={item.icon} sx={{ fontSize: 19 }} /></ListItemIcon>
                  <ListItemText primary={item.title} slotProps={{ primary: { sx: { fontSize: 12.5, fontWeight: 500 } } }} />
                </ListItemButton>)}
              </List>
            </Box>
          })}
        </Box>
        <Box sx={{ py: 2, px: 3, borderTop: '1px solid #27344b' }}><Typography variant="caption" sx={{ color: '#71839f' }}>Trade System</Typography><Typography sx={{ color: '#566b89', fontSize: 10 }}>Торгово-складська система</Typography></Box>
      </Box>
    </Drawer>
  )
}
