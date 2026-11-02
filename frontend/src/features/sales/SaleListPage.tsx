import { useCallback, useState } from 'react'
import { Box, Button, FormControl, InputLabel, Link as MuiLink, MenuItem, Paper, Select, Stack, TablePagination, TextField, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { usePriceTypes } from '../counterparties/queries'
import { dateLabel, locationName, money } from '../purchases/contracts'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DataTable } from '../../components/DataTable'
import type { DataColumn } from '../../components/DataTable'
import { FilterBar } from '../../components/FilterBar'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { PageHeader } from '../../components/PageHeader'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { SaleDetail } from './SaleDetailPage'
import { emptyFilters } from './contracts'
import type { SaleFilters } from './contracts'
import { useCustomers, useSaleLookups, useSales } from './queries'

export function SaleListPage() {
  const user = useCurrentUser().data
  const [filters, setFilters] = useState<SaleFilters>(emptyFilters); const [page, setPage] = useState(0); const [pageSize, setPageSize] = useState(10)
  const query = useSales({ ...filters, search: useDebouncedValue(filters.search) })
  const customers = useCustomers(); const lookups = useSaleLookups(); const priceTypes = usePriceTypes()
  const split = useMediaQuery(useTheme().breakpoints.up('lg'))
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [pendingId, setPendingId] = useState<string | null>(null)
  const [dirty, setDirty] = useState(false)
  const dirtyChanged = useCallback((value: boolean) => setDirty(value), [])
  const selectSale = (id: string) => { if (id === selectedId) return; if (dirty) setPendingId(id); else setSelectedId(id) }
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (!user) return null
  const canCreate = hasAnyPermission(user, ['sale.create'])
  const change = (key: keyof SaleFilters, value: string) => { setFilters(previous => ({ ...previous, [key]: value })); setPage(0) }
  const select = (key: 'counterpartyId' | 'storageLocationId' | 'priceTypeId' | 'status', label: string, values: { id: string; name: string }[]) => <FormControl size="small" sx={{ flex: '1 1 170px' }}><InputLabel id={`sale-filter-${key}`} shrink>{label}</InputLabel><Select labelId={`sale-filter-${key}`} label={label} value={filters[key]} displayEmpty notched onChange={event => change(key, event.target.value)}><MenuItem value="">Усі</MenuItem>{values.map(value => <MenuItem key={value.id} value={value.id}>{value.name}</MenuItem>)}</Select></FormControl>
  const rows = query.data ?? []; const currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1)); const visible = rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
  const locations = lookups.data?.locations ?? []
  const columns: DataColumn<(typeof rows)[number]>[] = [
    { key: 'number', label: 'Номер', width: split ? 112 : undefined, render: row => <Box><MuiLink component={Link} to={`/sales/${row.id}`} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }} onClick={event => { if (split && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); selectSale(row.id) } }}>{row.number || 'Без номера'}</MuiLink>{split && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', lineHeight: 1.2 }}>{dateLabel(row.documentDate)}</Typography>}</Box> },
    ...(!split ? [{ key: 'date', label: 'Дата', render: (row: (typeof rows)[number]) => dateLabel(row.documentDate) }] : []),
    { key: 'customer', label: 'Покупець', render: row => <Box title={`${row.counterpartyName} · ${locationName(row.storageLocationId, locations)} · ${row.priceTypeName || 'Без типу ціни'} · ${row.discountPercent}%`} sx={split ? { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' } : undefined}>{row.counterpartyName}</Box> },
    ...(!split ? [{ key: 'location', label: 'Склад', render: (row: (typeof rows)[number]) => locationName(row.storageLocationId, locations) }, { key: 'priceType', label: 'Тип ціни', render: (row: (typeof rows)[number]) => row.priceTypeName || '—' }] : []),
    { key: 'amount', label: 'Сума', width: split ? 80 : undefined, align: 'right', render: row => <Box sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{money(row.totalUah)}</Box> },
    ...(!split ? [{ key: 'discount', label: 'Знижка', render: (row: (typeof rows)[number]) => `${row.discountPercent}%` }] : []),
    { key: 'status', label: 'Статус', width: split ? 96 : undefined, render: row => <PurchaseStatus status={row.status} /> },
    { key: 'open', label: 'Дії', width: split ? 44 : undefined, render: row => <Button component={Link} to={`/sales/${row.id}`} size="small" aria-label="Відкрити" title="Відкрити окремо" sx={split ? { minWidth: 28, p: 0.5 } : undefined}>{split ? <Icon name="upRight" sx={{ fontSize: 18 }} /> : 'Відкрити'}</Button> },
  ]
  const list = <Stack spacing={2}>
    <PageHeader title="Продажі" description="Товари та послуги · UAH" actions={<><Button component={Link} to="/customer-returns" variant="outlined">Повернення покупців</Button>{canCreate && <Button component={Link} to="/sales/new" variant="contained" startIcon={<Icon name="plus" />}>Новий продаж</Button>}</>} />
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}><FilterBar mobileSheet><TextField label="Пошук" placeholder="Номер, покупець або коментар" value={filters.search} onChange={event => change('search', event.target.value)} sx={{ flex: '1 1 240px' }} />{(['from', 'to'] as const).map(key => <TextField key={key} label={key === 'from' ? 'Період від' : 'Період до'} type="date" value={filters[key]} onChange={event => change(key, event.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ flex: '1 1 145px' }} />)}<Box sx={{ width: '100%', display: 'flex', flexWrap: 'wrap', gap: 1.5 }}>{select('counterpartyId', 'Покупець', (customers.data ?? []).map(item => ({ id: item.id, name: item.name })))}{select('storageLocationId', 'Склад', locations.map(item => ({ id: item.id, name: locationName(item.id, locations) })))}{select('priceTypeId', 'Тип ціни', (priceTypes.data ?? []).map(item => ({ id: item.id, name: item.name })))}{select('status', 'Статус', [{ id: 'DRAFT', name: 'Чернетки' }, { id: 'POSTED', name: 'Проведені' }])}</Box></FilterBar>
      {(customers.isError || lookups.isError || priceTypes.isError) && <RequestErrorState error={customers.error ?? lookups.error ?? priceTypes.error} retry={() => { void customers.refetch(); void lookups.refetch(); void priceTypes.refetch() }} />}
      {query.isPending ? <ListLoading label="Завантаження продажів" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : !rows.length ? <EmptyState icon="sale" title={Object.values(filters).some(Boolean) ? 'Нічого не знайдено' : 'Продажів ще немає'} description="Створіть продаж або змініть фільтри." action={Object.values(filters).some(Boolean) ? <Button onClick={() => { setFilters(emptyFilters); setPage(0) }}>Скинути фільтри</Button> : canCreate ? <Button component={Link} to="/sales/new">Новий продаж</Button> : undefined} /> : <>
        {mobile ? <Stack component="ul" aria-label="Список продажів" sx={{ listStyle: 'none', p: 0, m: 0 }}>{visible.map(row => <MobileEntityCard key={row.id} title={<MuiLink component={Link} to={`/sales/${row.id}`} sx={{ fontWeight: 600, display: 'inline-flex', alignItems: 'center', minHeight: 44 }}>{row.number || 'Без номера'}</MuiLink>} status={<PurchaseStatus status={row.status} />} amount={`${money(row.totalUah)} UAH`}><Typography>{row.counterpartyName}</Typography><Typography variant="body2" color="text.secondary">{dateLabel(row.documentDate)} · {locationName(row.storageLocationId, locations)}</Typography></MobileEntityCard>)}</Stack> : <DataTable compact={split} label="Продажі" rows={visible} columns={columns} rowId={row => row.id} selectedId={selectedId} select={split ? row => selectSale(row.id) : undefined} />}
        <TablePagination component="div" count={rows.length} page={currentPage} rowsPerPage={pageSize} rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, value) => setPage(value)} onRowsPerPageChange={event => { setPageSize(Number(event.target.value)); setPage(0) }} labelRowsPerPage="На сторінці:" labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`} getItemAriaLabel={type => type === 'next' ? 'Наступна сторінка' : 'Попередня сторінка'} sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end' }, '.MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } } }} />
      </>}
    </Paper>
  </Stack>
  return <><SplitViewLayout detailLabel="Вибраний продаж" split={split} list={list} detail={selectedId ? <SaleDetail key={selectedId} id={selectedId} embedded onDirtyChange={dirtyChanged} onDeleted={() => { setSelectedId(null); setDirty(false) }} /> : <Paper variant="outlined"><EmptyState icon="sale" title="Оберіть продаж" description="Виберіть рядок у списку, щоб переглянути документ і виконати дії без переходу на іншу сторінку." /></Paper>} /><ConfirmDialog open={pendingId !== null} title="Незбережені зміни" action="Відкинути зміни" danger close={() => setPendingId(null)} confirm={() => { setSelectedId(pendingId); setPendingId(null); setDirty(false) }}>Поточний продаж має незбережені зміни. Відкинути їх і відкрити інший документ?</ConfirmDialog></>
}
