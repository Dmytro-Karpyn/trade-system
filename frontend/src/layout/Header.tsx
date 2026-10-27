import { useState } from 'react'
import { Alert, AppBar, Avatar, Box, Button, Divider, IconButton, InputAdornment, Menu, MenuItem, OutlinedInput, Snackbar, Toolbar, Tooltip, Typography } from '@mui/material'
import { useLogout } from '../auth/queries'
import type { CurrentUser } from '../auth/contracts'
import { Icon } from '../components/Icon'
import { tokens } from '../theme/tokens'

export function Header({ user, title, desktop, openMenu }: { user: CurrentUser; title: string; desktop: boolean; openMenu: () => void }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const logout = useLogout()
  const name = user.fullName || user.username
  const initials = name.split(/\s+/).map(part => part[0]).slice(0, 2).join('').toUpperCase()
  return <>
    <AppBar position="fixed" color="inherit" elevation={0} sx={{ width: desktop ? `calc(100% - ${tokens.sidebarWidth}px)` : '100%', borderBottom: '1px solid', borderColor: 'divider', bgcolor: '#fff' }}>
      <Toolbar sx={{ minHeight: `${tokens.headerHeight}px !important`, px: { xs: 2, md: 4 }, gap: 2 }}>
        {!desktop && <IconButton edge="start" aria-label="Відкрити меню" onClick={openMenu}><Icon name="menu" /></IconButton>}
        <Typography component="p" variant="subtitle1" noWrap sx={{ flexShrink: 1 }}>{title}</Typography>
        <Box sx={{ flex: 1 }} />
        <Tooltip title="Глобальний пошук буде доступний пізніше"><Box sx={{ display: { xs: 'none', lg: 'block' } }}><OutlinedInput disabled placeholder="Пошук у системі" inputProps={{ 'aria-label': 'Глобальний пошук — незабаром' }} startAdornment={<InputAdornment position="start"><Icon name="search" sx={{ fontSize: 18 }} /></InputAdornment>} sx={{ width: 232, height: 38, bgcolor: 'background.default', fontSize: 12, '& fieldset': { borderColor: 'divider' } }} /></Box></Tooltip>
        <Tooltip title="Швидке створення буде доступне пізніше"><Box sx={{ display: { xs: 'none', sm: 'block' } }}><Button variant="contained" aria-label="Швидке створення — незабаром" disabled startIcon={<Icon name="plus" />} sx={{ '&.Mui-disabled': { bgcolor: '#ecf2ff', color: '#7897d3' } }}>Створити</Button></Box></Tooltip>
        <Divider orientation="vertical" flexItem sx={{ my: 2.5, display: { xs: 'none', sm: 'block' } }} />
        <Button color="inherit" aria-label={`Меню користувача ${name}`} aria-controls={anchor ? 'user-menu' : undefined} aria-haspopup="true" aria-expanded={Boolean(anchor)} onClick={event => setAnchor(event.currentTarget)} sx={{ minWidth: 0, px: 0.5, gap: 1.25, textAlign: 'left' }}>
          <Avatar sx={{ bgcolor: '#eaf0fc', color: '#355eab', width: 34, height: 34, fontSize: 12, fontWeight: 700 }}>{initials}</Avatar>
          <Box sx={{ display: { xs: 'none', md: 'block' }, maxWidth: 165 }}><Typography noWrap sx={{ fontSize: 12, fontWeight: 650 }}>{name}</Typography><Typography noWrap sx={{ fontSize: 10, color: 'text.secondary' }}>@{user.username}</Typography></Box>
          <Icon name="down" sx={{ fontSize: 16, color: 'text.secondary' }} />
        </Button>
      </Toolbar>
    </AppBar>
    <Menu id="user-menu" anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'right' }}>
      <Box sx={{ px: 2, py: 1 }}><Typography variant="subtitle1">{name}</Typography><Typography variant="caption" color="text.secondary">{user.username}</Typography><Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>Організація</Typography><Typography sx={{ fontSize: 10, overflowWrap: 'anywhere', maxWidth: 270 }}>{user.organizationId}</Typography><Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>Ролі: {user.roles.join(', ') || 'Не призначено'}</Typography></Box>
      <Divider />
      <MenuItem disabled={logout.isPending} onClick={() => { setAnchor(null); logout.mutate() }}><Icon name="logout" sx={{ fontSize: 18, mr: 1.5 }} />{logout.isPending ? 'Виходимо…' : 'Вийти'}</MenuItem>
    </Menu>
    <Snackbar open={logout.isError} onClose={() => logout.reset()} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}><Alert severity="error" onClose={() => logout.reset()}>{logout.error?.message ?? 'Не вдалося вийти. Спробуйте ще раз.'}</Alert></Snackbar>
  </>
}
