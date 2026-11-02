import { Box, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Stack, Tooltip, Typography } from '@mui/material'
import { NavLink } from 'react-router-dom'
import { hasAnyPermission } from '../auth/contracts'
import type { CurrentUser } from '../auth/contracts'
import { Brand } from '../components/Brand'
import { Icon } from '../components/Icon'
import { navigation } from '../routes/navigation'
import { tokens } from '../theme/tokens'
import { mobilePrimaryPaths } from './mobileNavigation'

export function Sidebar({ user, desktop, open, close, collapsed = false, moreOnly = false }: { user: CurrentUser; desktop: boolean; open: boolean; close: () => void; collapsed?: boolean; moreOnly?: boolean }) {
  const compact = desktop && collapsed
  const allowed = navigation.filter(item => hasAnyPermission(user, item.permissions) && !(moreOnly && mobilePrimaryPaths.includes(item.path)))
  return <Drawer variant={desktop ? 'permanent' : 'temporary'} open={desktop || open} onClose={close} slotProps={{ paper: { sx: { width: desktop ? compact ? tokens.collapsedSidebarWidth : tokens.sidebarWidth : tokens.mobileSidebarWidth, maxWidth: 'calc(100vw - 32px)', bgcolor: tokens.colors.sidebar, color: tokens.colors.surface, border: 0 } } }}>
    <Box component="nav" aria-label="Основна навігація" sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', px: compact ? 2.25 : 2.5, minHeight: tokens.headerHeight, flexShrink: 0 }}><Brand light compact={compact} />{!desktop && <IconButton aria-label="Закрити меню" onClick={close} sx={{ color: tokens.colors.sidebarText, mr: -1 }}><Icon name="close" /></IconButton>}</Stack>
      {!compact && <Box sx={{ mx: 2, my: 1.5, p: 1.5, border: '1px solid', borderColor: tokens.colors.sidebarHover, borderRadius: 1 }}><Typography sx={{ fontSize: 12, fontWeight: 600 }}>Робочий простір</Typography><Typography title={user.organizationId} sx={{ fontSize: 12, color: tokens.colors.sidebarText }}>Організація · {user.organizationId.slice(0, 8)}</Typography></Box>}
      <Box sx={{ px: compact ? 1 : 1.5, py: 1, flex: 1, overflowY: 'auto' }}>{(['Робочий простір', 'Операції', 'Управління'] as const).map(group => {
        const items = allowed.filter(item => item.group === group)
        return items.length ? <Box key={group} sx={{ mb: compact ? 1.5 : 2 }}>{!compact && <Typography variant="overline" sx={{ color: tokens.colors.sidebarText, px: 1.5, display: 'block', my: 0.75 }}>{group}</Typography>}<List disablePadding>{items.map(item => <Tooltip key={item.path} title={compact ? item.title : ''} placement="right"><ListItemButton component={NavLink} to={item.path} end={item.path === '/'} aria-label={compact ? item.title : undefined} onClick={desktop ? undefined : close} sx={{ minHeight: 44, borderRadius: 1, mb: 0.5, px: 1.5, color: tokens.colors.sidebarText, '&:hover': { bgcolor: tokens.colors.sidebarHover, color: tokens.colors.surface }, '&.active': { bgcolor: tokens.colors.sidebarActive, color: tokens.colors.actionInk, fontWeight: 700 }, '&:focus-visible': { outlineColor: tokens.colors.surface, outlineOffset: -2 } }}><ListItemIcon sx={{ minWidth: compact ? 0 : 33, color: 'inherit' }}><Icon name={item.icon} sx={{ fontSize: 21 }} /></ListItemIcon>{!compact && <ListItemText primary={item.title} slotProps={{ primary: { sx: { fontSize: 13, fontWeight: 'inherit' } } }} />}</ListItemButton></Tooltip>)}</List></Box> : null
      })}</Box>
      {!compact && <Box sx={{ py: 1.5, px: 3, borderTop: '1px solid', borderColor: tokens.colors.sidebarHover }}><Typography variant="caption" sx={{ color: tokens.colors.sidebarText }}>Trade System · Робочий простір</Typography></Box>}
    </Box>
  </Drawer>
}
