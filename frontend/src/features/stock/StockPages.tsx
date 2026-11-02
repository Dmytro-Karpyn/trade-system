import { useState } from 'react'
import { Alert, Box, Button, Checkbox, Chip, Drawer, FormControlLabel, IconButton, MenuItem, Paper, Stack, Tab, Tabs, TextField, ToggleButton, ToggleButtonGroup, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link, useParams } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { tokens } from '../../theme/tokens'
import { ActiveBadge } from '../../components/ActiveBadge'
import { FilterBar } from '../../components/FilterBar'
import { Icon } from '../../components/Icon'
import { KpiCard } from '../../components/KpiCard'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { currencyName, dateLabel, locationName, money } from '../purchases/contracts'
import { priceRange, stockFilters, stockRowId } from './contracts'
import type { StockRow } from './contracts'
import { StockDocumentLink, StockMovements, StockReservations } from './StockHistory'
import { StockTable } from './StockTable'
import { useStockBatch, useStockLookups, useStockOverview, useStockProduct } from './queries'

function useStockRights() {
  const user = useCurrentUser().data
  const has = (permission: string) => Boolean(user && hasAnyPermission(user, [permission]))
  return { canView: has('stock.view'), canCost: has('cost.view'), canTransfer: has('stock.transfer'), canWriteOff: has('stock.writeoff') }
}
export function StockLandingPage() {
  const rights = useStockRights()
  return rights.canView ? <StockPage /> : <Stack spacing={2}><PageHeader title="Склад" description="Оберіть доступну складську операцію. Перегляд залишків потребує stock.view." /><Stack direction="row" spacing={1}>{rights.canTransfer && <Button component={Link} to="/stock/transfers" variant="contained">Переміщення</Button>}{rights.canWriteOff && <Button component={Link} to="/stock/write-offs" variant="contained">Списання</Button>}</Stack></Stack>
}
function PriceSelectors({ retail, wholesale, change }: { retail: string; wholesale: string; change: (key: 'retail' | 'wholesale', value: string) => void }) {
  const lookups = useStockLookups()
  return <>{(['retail', 'wholesale'] as const).map(key => <TextField key={key} select label={key === 'retail' ? 'Тип роздрібної ціни' : 'Тип оптової ціни'} value={key === 'retail' ? retail : wholesale} onChange={event => change(key, event.target.value)} sx={{ flex: '1 1 190px', minWidth: 0 }}><MenuItem value="">Не вибрано</MenuItem>{lookups.data?.priceTypes.map(type => <MenuItem value={type.id} key={type.id}>{type.name}{type.active ? '' : ' (неактивний)'}</MenuItem>)}</TextField>)}</>
}
export function StockPage() {
  const rights = useStockRights(), desktop = useMediaQuery(useTheme().breakpoints.up('lg'))
  const [filters, setFilters] = useState(stockFilters)
  const [selected, setSelected] = useState<{ key: string; productId: string; batchId: string | null; locationId: string } | null>(null)
  const query = useStockOverview({ ...filters, search: useDebouncedValue(filters.search, 300) }, rights.canCost), lookups = useStockLookups()
  // Quantities always come from the API row; do not sum quantities of mixed units.
  const current = query.data?.find(row => stockRowId(row) === selected?.key)
  const select = (row: StockRow) => setSelected({ key: stockRowId(row), productId: row.productId, batchId: row.batchId, locationId: row.storageLocationId })
  return <Stack spacing={2}>
    <PageHeader title="Склад" description="Фізичний залишок, резерви та доступна кількість" actions={<>{rights.canTransfer && <Button component={Link} to="/stock/transfers" variant="outlined">Переміщення</Button>}{rights.canWriteOff && <Button component={Link} to="/stock/write-offs" variant="outlined">Списання</Button>}</>} />
    {query.data && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' }, gap: 1.5 }}><KpiCard label="Товарів у вибірці" value={String(new Set(query.data.map(row => row.productId)).size)} />{current && <><KpiCard label={`Фізично · ${current.unit || 'обрана позиція'}`} value={String(current.physicalQuantity)} /><KpiCard label="Резерв · обрана позиція" value={String(current.reservedQuantity)} /><KpiCard label="Доступно · обрана позиція" value={String(current.availableQuantity)} /></>}</Box>}
    <Paper variant="outlined"><FilterBar mobileSheet><TextField label="Пошук" placeholder="Товар, код, штрихкод або партія" value={filters.search} onChange={event => setFilters({ ...filters, search: event.target.value })} sx={{ flex: '2 1 240px' }} /><TextField select label="Категорія" value={filters.categoryId} onChange={event => setFilters({ ...filters, categoryId: event.target.value })} disabled={!lookups.data?.categories.length} sx={{ flex: '1 1 180px' }} helperText={lookups.data?.categories.some(category => !category.name) ? 'Для попередніх посилань назви можуть бути відсутні.' : undefined}><MenuItem value="">Усі категорії</MenuItem>{lookups.data?.categories.map((category, index) => <MenuItem value={category.id} key={category.id}>{category.name || `Категорія без назви ${index + 1}`}</MenuItem>)}</TextField><TextField select label="Місце зберігання" value={filters.storageLocationId} onChange={event => setFilters({ ...filters, storageLocationId: event.target.value })} sx={{ flex: '1 1 180px' }}><MenuItem value="">Усі склади</MenuItem>{lookups.data?.locations.map(location => <MenuItem value={location.id} key={location.id}>{locationName(location.id, lookups.data.locations)}</MenuItem>)}</TextField>
      <PriceSelectors retail={filters.retailPriceTypeId} wholesale={filters.wholesalePriceTypeId} change={(key, value) => setFilters({ ...filters, [key === 'retail' ? 'retailPriceTypeId' : 'wholesalePriceTypeId']: value })} />
      <Stack direction="row" spacing={1.5} sx={{ width: '100%', alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}><ToggleButtonGroup size="small" exclusive value={filters.byBatch ? 'batches' : 'products'} onChange={(_, value: string | null) => { if (value) setFilters({ ...filters, byBatch: value === 'batches' }) }} aria-label="Групування складу"><ToggleButton value="products">По товарах</ToggleButton><ToggleButton value="batches">По партіях</ToggleButton></ToggleButtonGroup><FormControlLabel control={<Checkbox checked={filters.withStock} onChange={event => setFilters({ ...filters, withStock: event.target.checked })} />} label="Тільки з залишком" /><FormControlLabel control={<Checkbox checked={filters.withReservations} onChange={event => setFilters({ ...filters, withReservations: event.target.checked })} />} label="Тільки з резервом" /><Chip label="Товари · GOODS" size="small" variant="outlined" /></Stack><Typography variant="caption" color="text.secondary">Ціни в UAH. Для різних цін партій показано діапазон; типи цін вибираються явно.</Typography>
    </FilterBar>{lookups.isError && <RequestErrorState error={lookups.error} retry={() => { void lookups.refetch() }} />}{query.isPending ? <ListLoading label="Завантаження складу" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : <StockTable rows={query.data} canCost={rights.canCost} batches={filters.byBatch} selectedId={selected?.key} select={desktop ? select : undefined} />}</Paper>
    <Drawer anchor="right" variant="persistent" open={desktop && Boolean(selected)} slotProps={{ paper: { sx: { top: tokens.headerHeight, width: 'min(640px, 52vw)', height: `calc(100dvh - ${tokens.headerHeight}px)`, bgcolor: 'background.default', borderLeft: '1px solid', borderColor: 'divider', p: 2.5 } } }}><Box component="section" aria-label="Вибрана складська позиція" onKeyDown={event => { if (event.key === 'Escape') setSelected(null) }} sx={{ minWidth: 0 }}><Stack direction="row" sx={{ justifyContent: 'space-between', mb: 1 }}><Button component={Link} to={selected?.batchId ? `/stock/batches/${selected.batchId}` : `/stock/products/${selected?.productId}`} startIcon={<Icon name="upRight" />}>Відкрити окремо</Button><IconButton aria-label="Закрити складську позицію" onClick={() => setSelected(null)}><Icon name="close" /></IconButton></Stack>{selected && (selected.batchId ? <StockBatchDetail key={selected.batchId} batchId={selected.batchId} embedded /> : <StockProductDetail key={selected.productId} productId={selected.productId} embedded locationId={selected.locationId} />)}</Box></Drawer>
  </Stack>
}
export function StockProductPage() { const { productId = '' } = useParams(); return <StockProductDetail productId={productId} /> }
export function StockProductDetail({ productId, embedded = false, hideHeader = false, initialTab = 0, locationId, fixedTab, hideAvailable = false }: { fixedTab?: number; hideAvailable?: boolean; productId: string; embedded?: boolean; hideHeader?: boolean; initialTab?: number; locationId?: string }) {
  const { canCost } = useStockRights(), [tab, setTab] = useState(initialTab), [prices, setPrices] = useState({ retail: '', wholesale: '' })
  const query = useStockProduct(productId, prices.retail, prices.wholesale, canCost)
  if (query.isPending) return <ListLoading label="Завантаження товару на складі" />
  if (query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const { product, balances, batches } = query.data
  const activeTab = fixedTab ?? tab
  const selectors = <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ p: 2 }}><PriceSelectors {...prices} change={(key, value) => setPrices({ ...prices, [key]: value })} /></Stack>
  return <Stack spacing={2}>
    {!embedded && <Button component={Link} to="/stock" startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>До складу</Button>}
    {!hideHeader && <PageHeader secondary={embedded} title={product.name} description={<Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}><Typography variant="body2">{product.code} · {product.unit || 'Одиницю не названо'} · {product.category || (product.categoryId ? 'Категорія: назву не задано' : 'Без категорії')}</Typography><ActiveBadge active={product.active} /></Stack>} />}
    <Paper variant="outlined">{fixedTab === undefined && <Tabs value={activeTab} onChange={(_, value: number) => setTab(value)} variant="scrollable" scrollButtons="auto" aria-label="Дані товару на складі">{['Залишки', 'Партії', 'Рух товару', 'Ціни', 'Резерви'].map(label => <Tab label={label} key={label} />)}</Tabs>}
      {activeTab === 0 && <StockTable rows={balances} canCost={false} showPrices={false} compact={embedded} />}
      {activeTab === 1 && <>{selectors}<StockTable rows={batches} canCost={canCost} batches compact={embedded} /></>}
      {activeTab === 2 && <StockMovements scope={{ productId, storageLocationId: locationId }} />}
      {activeTab === 3 && <>{selectors}<Alert severity="info" sx={{ mx: 2, mb: 2 }}>Поточні ціни партій. Для повного переліку типів цін відкрийте партію. Зміна цін тут не виконується.</Alert><StockTable rows={batches} canCost={false} batches compact={embedded} /></>}
      {activeTab === 4 && <StockReservations scope={{ productId, storageLocationId: locationId }} />}
    </Paper>
    {!hideAvailable && <Paper variant="outlined"><Box sx={{ p: 2 }}><Typography variant="h2" component="h2">Доступні партії</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>Найстаріші закупівлі першими, тільки доступна кількість більше нуля.</Typography></Box><StockTable rows={[...batches].filter(row => row.availableQuantity > 0).sort((a, b) => (a.purchaseDocumentDate ?? '').localeCompare(b.purchaseDocumentDate ?? ''))} canCost={canCost} batches compact={embedded} /></Paper>}
  </Stack>
}
export function StockBatchPage() { const { batchId = '' } = useParams(); return <StockBatchDetail batchId={batchId} /> }
export function StockBatchDetail({ batchId, embedded = false }: { batchId: string; embedded?: boolean }) {
  const { canCost } = useStockRights(), query = useStockBatch(batchId, canCost), [tab, setTab] = useState(0)
  if (query.isPending) return <ListLoading label="Завантаження партії" />
  if (query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const batch = query.data
  const values: [string, React.ReactNode][] = [['Товар', <Link to={`/stock/products/${batch.product.id}`}>{batch.product.name}</Link>], ['Закупівля', <StockDocumentLink id={batch.purchaseDocumentId} type={batch.purchaseDocumentType} number={batch.purchaseNumber} />], ['Постачальник', batch.supplier || '—'], ['Дата закупівлі', dateLabel(batch.purchaseDocumentDate)], ['Валюта закупівлі', currencyName(batch.currencyId, [batch.currencyId])]]
  if (canCost && batch.valuation) values.push(['Закупівельна ціна за одиницю', money(batch.valuation.unitPriceCurrency)], ['Курс', String(batch.valuation.exchangeRate)], ['Знижка закупівлі', `${batch.valuation.discountPercent}%`], ['Поточна собівартість', `${money(batch.valuation.unitCostUah)} UAH`])
  return <Stack spacing={2}>{!embedded && <Button component={Link} to="/stock" startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>До складу</Button>}<PageHeader title={batch.batchNumber} secondary={embedded} />
    <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 2 }}>Походження партії</Typography><Box component="dl" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: embedded ? '160px minmax(0, 1fr)' : '230px minmax(0, 1fr)' }, gap: 1.5, m: 0 }}>{values.map(([label, value]) => <Box key={label} sx={{ display: 'contents' }}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" variant="body2" sx={{ m: 0, overflowWrap: 'anywhere' }}>{value}</Typography></Box>)}</Box></Paper>
    <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 1.5 }}>Ціни партії</Typography>{!batch.prices.length ? <EmptyState title="Ціни ще не задані." /> : <Stack spacing={1}>{batch.prices.map(price => <Stack key={price.priceTypeId} direction="row" sx={{ justifyContent: 'space-between', gap: 1, py: 0.5 }}><Typography variant="body2">{price.priceTypeName}</Typography><Typography variant="body2">{priceRange({ min: price.unitPrice, max: price.unitPrice })} UAH</Typography></Stack>)}</Stack>}</Paper>
    <Paper variant="outlined"><Typography variant="h2" component="h2" sx={{ p: 2 }}>Залишки за складами</Typography><StockTable rows={batch.balances} canCost={false} showPrices={false} compact={embedded} /></Paper>
    <Paper variant="outlined"><Tabs value={tab} onChange={(_, value: number) => setTab(value)} variant="scrollable" aria-label="Операції партії"><Tab label="Рух партії" /><Tab label="Резерви" /></Tabs>{tab === 0 ? <StockMovements scope={{ batchId }} /> : <StockReservations scope={{ batchId }} />}</Paper>
  </Stack>
}
