import { useCallback, useState } from 'react'
import { Box, Button, FormControl, IconButton, InputAdornment, InputLabel, Link as MuiLink, MenuItem, Paper, Select, Stack, TablePagination, TextField, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DataTable } from '../../components/DataTable'
import type { DataColumn } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { FilterBar } from '../../components/FilterBar'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { currencyName, dateLabel, emptyFilters, locationName, money } from './contracts'
import type { PurchaseFilters } from './contracts'
import { PurchaseDetail } from './PurchaseDetailPage'
import { PurchaseStatus } from './PurchaseStatus'
import { usePurchaseLookups, usePurchases, usePurchaseSuppliers } from './queries'

export function PurchaseListPage() {
  const user = useCurrentUser().data
  const [filters, setFilters] = useState<PurchaseFilters>(emptyFilters)
  const [page, setPage] = useState(0), [pageSize, setPageSize] = useState(10)
  const [selectedId, setSelectedId] = useState<string | null>(null), [pendingId, setPendingId] = useState<string | null>(null)
  const [dirty, setDirty] = useState(false)
  const dirtyChanged = useCallback((value: boolean) => setDirty(value), [])
  const list = usePurchases({ ...filters, search: useDebouncedValue(filters.search) })
  const lookups = usePurchaseLookups(), suppliers = usePurchaseSuppliers()
  const mobile = useMediaQuery(useTheme().breakpoints.down('md')), split = useMediaQuery(useTheme().breakpoints.up('lg'))
  if (!user) return null
  const canCreate = hasAnyPermission(user, ['purchase.create'])
  const update = (key: keyof PurchaseFilters, value: string) => { setFilters(previous => ({ ...previous, [key]: value })); setPage(0) }
  const selectPurchase = (id: string) => { if (id === selectedId) return; if (dirty) setPendingId(id); else setSelectedId(id) }
  const select = (name: 'counterpartyId' | 'storageLocationId' | 'currencyId' | 'status', label: string, options: { id: string; name: string }[]) => <FormControl size="small" sx={{ flex: '1 1 170px' }}><InputLabel id={`purchase-filter-${name}`} shrink>{label}</InputLabel><Select labelId={`purchase-filter-${name}`} label={label} displayEmpty notched value={filters[name]} onChange={event => update(name, event.target.value)}><MenuItem value="">Усі</MenuItem>{options.map(option => <MenuItem key={option.id} value={option.id}>{option.name}</MenuItem>)}</Select></FormControl>
  const rows = list.data ?? [], currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1))
  const visible = rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
  const locations = lookups.data?.locations ?? [], currencies = lookups.data?.currencyIds ?? []
  const filtered = Object.values(filters).some(Boolean)
  const columns: DataColumn<(typeof rows)[number]>[] = [
    { key: 'number', label: 'Номер', width: split ? 112 : undefined, render: row => <Box><MuiLink component={Link} to={`/purchases/${row.id}`} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }} onClick={event => { if (split && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); selectPurchase(row.id) } }}>{row.number || 'Без номера'}</MuiLink>{split && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', lineHeight: 1.2 }}>{dateLabel(row.documentDate)}</Typography>}</Box> },
    ...(!split ? [{ key: 'date', label: 'Дата', render: (row: (typeof rows)[number]) => dateLabel(row.documentDate) }] : []),
    { key: 'supplier', label: 'Постачальник', render: row => <Box sx={split ? { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } : undefined} title={row.counterpartyName || undefined}>{row.counterpartyName || '—'}</Box> },
    ...(!split ? [{ key: 'location', label: 'Склад', render: (row: (typeof rows)[number]) => locationName(row.storageLocationId, locations) }, { key: 'currency', label: 'Валюта', render: (row: (typeof rows)[number]) => currencyName(row.currencyId, currencies) }] : []),
    { key: 'amount', label: 'Сума', width: split ? 90 : undefined, align: 'right', render: row => <Box sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{money(row.totalCurrency)}{split && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', lineHeight: 1.2 }}>{currencyName(row.currencyId, currencies)}</Typography>}</Box> },
    { key: 'status', label: 'Статус', width: split ? 96 : undefined, render: row => <PurchaseStatus status={row.status} /> },
    ...(!split ? [{ key: 'external', label: 'Зовнішній номер', render: (row: (typeof rows)[number]) => row.externalNumber || '—' }] : []),
    { key: 'open', label: 'Дії', width: split ? 40 : undefined, render: row => <IconButton component={Link} to={`/purchases/${row.id}`} size="small" title="Відкрити окремо" aria-label={`Відкрити ${row.number || 'чернетку'}`}><Icon name="upRight" sx={{ fontSize: 18 }} /></IconButton> },
  ]
  const content = <Stack spacing={2}>
    <PageHeader title="Закупівлі" description="Прихідні накладні та надходження товарів" actions={<><Button component={Link} to="/supplier-returns" variant="outlined">Повернення постачальникам</Button>{canCreate && <Button component={Link} to="/purchases/new" variant="contained" startIcon={<Icon name="plus" />}>Нова закупівля</Button>}</>} />
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <FilterBar mobileSheet><TextField label="Пошук" placeholder="Номер, постачальник або зовнішній номер" value={filters.search} onChange={event => update('search', event.target.value)} slotProps={{ input: { startAdornment: <InputAdornment position="start"><Icon name="search" sx={{ fontSize: 18 }} /></InputAdornment> } }} sx={{ flex: '1 1 240px' }} />
        {(['from', 'to'] as const).map(key => <TextField key={key} type="date" label={key === 'from' ? 'Період від' : 'Період до'} value={filters[key]} onChange={event => update(key, event.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ flex: '1 1 145px' }} />)}
        <Box sx={{ width: '100%', display: 'flex', flexWrap: 'wrap', gap: 1.5 }}>{select('counterpartyId', 'Постачальник', (suppliers.data ?? []).map(item => ({ id: item.id, name: item.name })))}{select('storageLocationId', 'Склад', locations.map(item => ({ id: item.id, name: locationName(item.id, locations) })))}{select('currencyId', 'Валюта', currencies.map(id => ({ id, name: currencyName(id, currencies) })))}{select('status', 'Статус', [{ id: 'DRAFT', name: 'Чернетки' }, { id: 'POSTED', name: 'Проведені' }])}</Box>
      </FilterBar>
      {(lookups.isError || suppliers.isError) && <RequestErrorState error={lookups.error ?? suppliers.error} retry={() => { void lookups.refetch(); void suppliers.refetch() }} />}
      {list.isPending ? <ListLoading label="Завантаження закупівель" /> : list.isError ? <RequestErrorState error={list.error} retry={() => { void list.refetch() }} /> : !rows.length ? <EmptyState icon="purchase" title={filtered ? 'Нічого не знайдено' : 'Закупівель ще немає'} description={filtered ? 'Змініть пошук або фільтри.' : 'Створіть першу прихідну накладну.'} action={filtered ? <Button onClick={() => { setFilters(emptyFilters); setPage(0) }}>Скинути фільтри</Button> : canCreate ? <Button component={Link} to="/purchases/new">Нова закупівля</Button> : undefined} /> : <>
        {mobile ? <Box component="ul" aria-label="Список закупівель" sx={{ listStyle: 'none', p: 0, m: 0 }}>{visible.map(row => <MobileEntityCard key={row.id} title={<MuiLink component={Link} to={`/purchases/${row.id}`} sx={{ fontWeight: 600, display: 'inline-flex', alignItems: 'center', minHeight: 44 }}>{row.number || 'Без номера'}</MuiLink>} status={<PurchaseStatus status={row.status} />} amount={`${money(row.totalCurrency)} ${currencyName(row.currencyId, currencies)}`}><Typography>{row.counterpartyName || 'Постачальника не вказано'}</Typography><Typography variant="body2" color="text.secondary">{dateLabel(row.documentDate)} · {locationName(row.storageLocationId, locations)}</Typography></MobileEntityCard>)}</Box> : <DataTable label="Закупівлі" compact={split} rows={visible} columns={columns} rowId={row => row.id} selectedId={selectedId} select={split ? row => selectPurchase(row.id) : undefined} />}
        <TablePagination component="div" count={rows.length} page={currentPage} rowsPerPage={pageSize} rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, value) => setPage(value)} onRowsPerPageChange={event => { setPageSize(Number(event.target.value)); setPage(0) }} labelRowsPerPage="На сторінці:" labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`} getItemAriaLabel={type => type === 'next' ? 'Наступна сторінка' : 'Попередня сторінка'} sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end' }, '.MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } } }} />
      </>}
    </Paper>
  </Stack>
  return <><SplitViewLayout split={split} detailLabel="Вибрана закупівля" list={content} detail={selectedId ? <PurchaseDetail key={selectedId} id={selectedId} embedded onDirtyChange={dirtyChanged} onDeleted={() => { setSelectedId(null); setDirty(false) }} /> : <Paper variant="outlined"><EmptyState icon="purchase" title="Оберіть закупівлю" description="Виберіть рядок, щоб працювати з накладною поруч зі списком." /></Paper>} /><ConfirmDialog open={pendingId !== null} title="Незбережені зміни" action="Відкинути зміни" danger close={() => setPendingId(null)} confirm={() => { setSelectedId(pendingId); setPendingId(null); setDirty(false) }}>Відкинути незбережені зміни закупівлі й відкрити інший документ?</ConfirmDialog></>
}
