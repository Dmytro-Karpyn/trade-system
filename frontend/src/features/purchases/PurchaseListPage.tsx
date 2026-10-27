import { useState } from 'react'
import { Box, Button, FormControl, IconButton, InputAdornment, InputLabel, Link as MuiLink, MenuItem, Paper, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, TextField, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { currencyName, dateLabel, emptyFilters, locationName, money } from './contracts'
import type { PurchaseFilters } from './contracts'
import { PurchaseStatus } from './PurchaseStatus'
import { usePurchaseLookups, usePurchases, usePurchaseSuppliers } from './queries'

export function PurchaseListPage() {
  const user = useCurrentUser().data
  const [filters, setFilters] = useState<PurchaseFilters>(emptyFilters)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const search = useDebouncedValue(filters.search)
  const list = usePurchases({ ...filters, search })
  const lookups = usePurchaseLookups()
  const suppliers = usePurchaseSuppliers()
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (!user) return null
  const canCreate = hasAnyPermission(user, ['purchase.create'])
  const update = (key: keyof PurchaseFilters, value: string) => { setFilters(previous => ({ ...previous, [key]: value })); setPage(0) }
  const select = (name: 'counterpartyId' | 'storageLocationId' | 'currencyId' | 'status', label: string, options: { id: string; name: string }[]) => <FormControl size="small" sx={{ flex: '1 1 170px' }}><InputLabel id={`purchase-filter-${name}`} shrink>{label}</InputLabel><Select labelId={`purchase-filter-${name}`} label={label} displayEmpty notched value={filters[name]} onChange={event => update(name, event.target.value)}><MenuItem value="">Усі</MenuItem>{options.map(option => <MenuItem key={option.id} value={option.id}>{option.name}</MenuItem>)}</Select></FormControl>
  const rows = list.data ?? []
  const currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1))
  const visible = rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
  const locations = lookups.data?.locations ?? []
  const currencies = lookups.data?.currencyIds ?? []
  const filtered = Object.values(filters).some(Boolean)
  return <Stack spacing={3}>
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { xs: 'flex-start', sm: 'center' }, justifyContent: 'space-between' }}><Box><Typography variant="h1" component="h1">Закупівлі</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Прихідні накладні та надходження товарів</Typography></Box>{canCreate && <Button component={Link} to="/purchases/new" variant="contained" startIcon={<Icon name="plus" />}>Нова закупівля</Button>}</Stack>
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <Box sx={{ p: 2.5, display: 'flex', flexWrap: 'wrap', gap: 2 }}>
        <TextField label="Пошук" placeholder="Номер, постачальник або зовнішній номер" value={filters.search} onChange={event => update('search', event.target.value)} slotProps={{ input: { startAdornment: <InputAdornment position="start"><Icon name="search" sx={{ fontSize: 18 }} /></InputAdornment> } }} sx={{ flex: '1 1 300px' }} />
        {(['from', 'to'] as const).map(key => <TextField key={key} type="date" label={key === 'from' ? 'Період від' : 'Період до'} value={filters[key]} onChange={event => update(key, event.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ flex: '1 1 145px' }} />)}
        <Box sx={{ width: '100%', display: 'flex', flexWrap: 'wrap', gap: 2 }}>{select('counterpartyId', 'Постачальник', (suppliers.data ?? []).map(item => ({ id: item.id, name: item.name })))}{select('storageLocationId', 'Склад', locations.map(item => ({ id: item.id, name: locationName(item.id, locations) })))}{select('currencyId', 'Валюта', currencies.map(id => ({ id, name: currencyName(id, currencies) })))}{select('status', 'Статус', [{ id: 'DRAFT', name: 'Чернетки' }, { id: 'POSTED', name: 'Проведені' }])}</Box>
      </Box>
      {(lookups.isError || suppliers.isError) && <RequestErrorState error={lookups.error ?? suppliers.error} retry={() => { void lookups.refetch(); void suppliers.refetch() }} />}
      {list.isPending ? <ListLoading label="Завантаження закупівель" /> : list.isError ? <RequestErrorState error={list.error} retry={() => { void list.refetch() }} /> : !rows.length ? <EmptyState icon="purchase" title={filtered ? 'Нічого не знайдено' : 'Закупівель ще немає'} description={filtered ? 'Змініть пошук або фільтри.' : 'Створіть першу прихідну накладну.'} action={filtered ? <Button onClick={() => { setFilters(emptyFilters); setPage(0) }}>Скинути фільтри</Button> : canCreate ? <Button component={Link} to="/purchases/new">Нова закупівля</Button> : undefined} /> : <>
        {mobile ? <Box component="ul" aria-label="Список закупівель" sx={{ listStyle: 'none', p: 0, m: 0 }}>{visible.map(row => <Box component="li" key={row.id} sx={{ p: 2.5, borderTop: '1px solid', borderColor: 'divider' }}><Stack direction="row" sx={{ justifyContent: 'space-between', gap: 1 }}><MuiLink component={Link} to={`/purchases/${row.id}`} sx={{ fontWeight: 600 }}>{row.number || 'Без номера'}</MuiLink><PurchaseStatus status={row.status} /></Stack><Typography sx={{ mt: 1 }}>{row.counterpartyName || 'Постачальника не вказано'}</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{dateLabel(row.documentDate)} · {locationName(row.storageLocationId, locations)}</Typography><Typography sx={{ mt: 1.5, fontWeight: 600 }}>{money(row.totalCurrency)} {currencyName(row.currencyId, currencies)}</Typography></Box>)}</Box> : <TableContainer><Table aria-label="Закупівлі" sx={{ minWidth: 1050 }}><TableHead><TableRow>{['Номер', 'Дата', 'Постачальник', 'Склад', 'Валюта', 'Сума', 'Статус', 'Зовнішній номер', 'Дії'].map(label => <TableCell key={label} align={label === 'Сума' ? 'right' : 'left'}>{label}</TableCell>)}</TableRow></TableHead><TableBody>{visible.map(row => <TableRow hover key={row.id}><TableCell><MuiLink component={Link} to={`/purchases/${row.id}`} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{row.number || 'Без номера'}</MuiLink></TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{dateLabel(row.documentDate)}</TableCell><TableCell>{row.counterpartyName || '—'}</TableCell><TableCell>{locationName(row.storageLocationId, locations)}</TableCell><TableCell>{currencyName(row.currencyId, currencies)}</TableCell><TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>{money(row.totalCurrency)}</TableCell><TableCell><PurchaseStatus status={row.status} /></TableCell><TableCell>{row.externalNumber || '—'}</TableCell><TableCell><Tooltip title="Відкрити"><IconButton component={Link} to={`/purchases/${row.id}`} size="small" aria-label={`Відкрити ${row.number || 'чернетку'}`}><Icon name="eye" sx={{ fontSize: 18 }} /></IconButton></Tooltip></TableCell></TableRow>)}</TableBody></Table></TableContainer>}
        <TablePagination component="div" count={rows.length} page={currentPage} rowsPerPage={pageSize} rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, value) => setPage(value)} onRowsPerPageChange={event => { setPageSize(Number(event.target.value)); setPage(0) }} labelRowsPerPage="На сторінці:" labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`} getItemAriaLabel={type => type === 'next' ? 'Наступна сторінка' : 'Попередня сторінка'} sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end' }, '.MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } } }} />
      </>}
    </Paper>
  </Stack>
}
