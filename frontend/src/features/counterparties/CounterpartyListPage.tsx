import { useState } from 'react'
import { Alert, Box, Button, Divider, FormControl, IconButton, InputAdornment, InputLabel, Link as MuiLink, MenuItem, Paper, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, TextField, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ActiveBadge } from '../../components/ActiveBadge'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { PartyBadges } from './CounterpartyBadges'
import { legalTypes, priceTypeName } from './contracts'
import type { Counterparty, PartyFilters } from './contracts'
import { useCounterparties, usePriceTypes } from './queries'

export function CounterpartyListPage() {
  const user = useCurrentUser().data
  const [search, setSearch] = useState('')
  const [role, setRole] = useState<PartyFilters['role']>('')
  const [active, setActive] = useState<PartyFilters['active']>('true')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const debounced = useDebouncedValue(search)
  const parties = useCounterparties({ search: debounced, role, active })
  const prices = usePriceTypes()
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (!user) return null
  const canWrite = hasAnyPermission(user, ['settings.manage'])
  const rows = parties.data ?? []
  const currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1))
  const visible = rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
  const filtered = Boolean(search.trim() || role || active !== 'true')
  const actions = (party: Counterparty) => <Stack direction="row" spacing={0.5}><Tooltip title="Переглянути"><IconButton component={Link} to={`/counterparties/${party.id}`} size="small" aria-label={`Переглянути ${party.name}`}><Icon name="eye" sx={{ fontSize: 18 }} /></IconButton></Tooltip>{canWrite && <Tooltip title="Редагувати"><IconButton component={Link} to={`/counterparties/${party.id}`} state={{ edit: true }} size="small" aria-label={`Редагувати ${party.name}`}><Icon name="edit" sx={{ fontSize: 18 }} /></IconButton></Tooltip>}</Stack>
  return <Stack spacing={3}>
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}><Box><Typography variant="h1" component="h1">Контрагенти</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Покупці, постачальники та працівники</Typography></Box>{canWrite && <Button component={Link} to="/counterparties/new" variant="contained" startIcon={<Icon name="plus" />}>Додати контрагента</Button>}</Stack>
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <Box sx={{ p: 2.5, display: 'flex', flexWrap: 'wrap', gap: 2 }}>
        <TextField label="Пошук" placeholder="Назва, телефон, email або код" value={search} onChange={event => { setSearch(event.target.value); setPage(0) }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><Icon name="search" sx={{ fontSize: 18 }} /></InputAdornment> } }} sx={{ flex: '1 1 260px' }} />
        <FormControl size="small" sx={{ flex: { xs: '1 1 145px', sm: '0 0 175px' } }}><InputLabel id="party-role-filter" shrink>Ролі</InputLabel><Select labelId="party-role-filter" label="Ролі" displayEmpty notched value={role} onChange={event => { setRole(event.target.value as PartyFilters['role']); setPage(0) }}><MenuItem value="">Усі</MenuItem><MenuItem value="customer">Покупці</MenuItem><MenuItem value="supplier">Постачальники</MenuItem><MenuItem value="employee">Працівники</MenuItem><MenuItem value="system">Системні</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ flex: { xs: '1 1 145px', sm: '0 0 160px' } }}><InputLabel id="party-active-filter" shrink>Статус</InputLabel><Select labelId="party-active-filter" label="Статус" displayEmpty notched value={active} onChange={event => { setActive(event.target.value as PartyFilters['active']); setPage(0) }}><MenuItem value="true">Активні</MenuItem><MenuItem value="false">Неактивні</MenuItem><MenuItem value="">Усі</MenuItem></Select></FormControl>
      </Box>
      {prices.permitted && prices.isError && <Alert severity="warning" action={<Button onClick={() => { void prices.refetch() }}>Повторити</Button>} sx={{ mx: 2.5, mb: 2 }}>Не вдалося завантажити типи цін.</Alert>}
      {parties.isPending ? <ListLoading label="Завантаження контрагентів" /> : parties.isError ? <RequestErrorState error={parties.error} retry={() => { void parties.refetch() }} /> : !rows.length ? <EmptyState icon="people" title={filtered ? 'Нічого не знайдено' : 'Контрагентів ще немає'} description={filtered ? 'Змініть пошук або фільтри.' : 'Додайте першого контрагента.'} action={filtered ? <Button onClick={() => { setSearch(''); setRole(''); setActive('true'); setPage(0) }}>Скинути фільтри</Button> : canWrite ? <Button component={Link} to="/counterparties/new" variant="outlined">Додати контрагента</Button> : undefined} /> : <>
        {mobile ? <Box component="ul" aria-label="Список контрагентів" sx={{ listStyle: 'none', p: 0, m: 0 }}>{visible.map(party => <Box component="li" key={party.id} sx={{ p: 2.5, borderTop: '1px solid', borderColor: 'divider' }}><Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'flex-start', gap: 1 }}><MuiLink component={Link} to={`/counterparties/${party.id}`} underline="hover" sx={{ color: 'text.primary', fontWeight: 600, overflowWrap: 'anywhere' }}>{party.name}</MuiLink>{actions(party)}</Stack><Box sx={{ mt: 1 }}><PartyBadges party={party} /></Box><Stack direction="row" spacing={1.5} sx={{ mt: 1.5, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', rowGap: 1 }}><Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{party.phone || 'Телефон не вказано'}</Typography><ActiveBadge active={party.active} /></Stack></Box>)}</Box> : <TableContainer><Table aria-label="Контрагенти" sx={{ minWidth: 1000 }}><TableHead><TableRow>{['Назва', 'Тип', 'Ролі', 'Телефон', 'Email', 'Тип ціни', 'Активний', 'Дії'].map(label => <TableCell key={label}>{label}</TableCell>)}</TableRow></TableHead><TableBody>{visible.map(party => <TableRow key={party.id} hover><TableCell sx={{ minWidth: 170 }}><MuiLink component={Link} to={`/counterparties/${party.id}`} underline="hover" sx={{ color: 'text.primary', fontWeight: 600, overflowWrap: 'anywhere' }}>{party.name}</MuiLink></TableCell><TableCell>{party.legalType ? legalTypes[party.legalType] : '—'}</TableCell><TableCell sx={{ minWidth: 155 }}><PartyBadges party={party} />{!party.isCustomer && !party.isSupplier && !party.isEmployee && !party.isSystem && '—'}</TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{party.phone || '—'}</TableCell><TableCell sx={{ overflowWrap: 'anywhere', maxWidth: 180 }}>{party.email || '—'}</TableCell><TableCell>{priceTypeName(party.defaultPriceTypeId, prices.data ?? [])}</TableCell><TableCell><ActiveBadge active={party.active} /></TableCell><TableCell>{actions(party)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>}
        <Divider /><TablePagination component="div" count={rows.length} page={currentPage} rowsPerPage={pageSize} rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, value) => setPage(value)} onRowsPerPageChange={event => { setPageSize(Number(event.target.value)); setPage(0) }} labelRowsPerPage="На сторінці:" labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`} getItemAriaLabel={type => type === 'next' ? 'Наступна сторінка' : 'Попередня сторінка'} sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end' }, '.MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } } }} />
      </>}
    </Paper>
  </Stack>
}
