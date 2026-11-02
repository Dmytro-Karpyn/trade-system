import { useCategories } from '../directories/queries'
import { directoryTree } from '../directories/contracts'
import { useCallback, useState } from 'react'
import { Alert, Box, Button, Divider, FormControl, IconButton, InputAdornment, InputLabel, LinearProgress, Link as MuiLink, MenuItem, Paper, Select, Stack, TablePagination, TextField, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { useCurrentUser } from '../../auth/queries'
import { hasAnyPermission } from '../../auth/contracts'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DataTable } from '../../components/DataTable'
import type { DataColumn } from '../../components/DataTable'
import { FilterBar } from '../../components/FilterBar'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { PageHeader } from '../../components/PageHeader'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { ProductDetail } from './ProductDetailPage'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { RequestErrorState } from '../../components/RequestState'
import { ActiveBadge } from '../../components/ActiveBadge'
import { TypeBadge } from './ProductBadges'
import { ListLoading } from '../../components/ListLoading'
import { unitName } from './contracts'
import type { Product, ProductFilters } from './contracts'
import { useProducts, useUnits } from './queries'
import { useDebouncedValue } from '../../components/useDebouncedValue'

export function ProductListPage() {
  const user = useCurrentUser().data
  const [search, setSearch] = useState('')
  const [itemType, setItemType] = useState<ProductFilters['itemType']>('')
  const [active, setActive] = useState<ProductFilters['active']>('true')
  const [categoryId, setCategoryId] = useState('')
  const [selected, setSelected] = useState<{ id: string; edit: boolean } | null>(null), [pending, setPending] = useState<typeof selected>(null)
  const [dirty, setDirty] = useState(false)
  const dirtyChanged = useCallback((value: boolean) => setDirty(value), [])
  const split = useMediaQuery(useTheme().breakpoints.up('lg'))
  const selectProduct = (id: string, edit = false) => { if (id === selected?.id && (!edit || selected.edit)) return; if (dirty) setPending({ id, edit }); else setSelected({ id, edit }) }
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const debouncedSearch = useDebouncedValue(search)
  const products = useProducts({ search: debouncedSearch, itemType, active })
  const units = useUnits(); const categories=useCategories(); const categoryName=(id:string)=>directoryTree(categories.data??[]).find(n=>n.node.id===id)?.path??'Назву не вказано'
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (!user) return null
  const canCreate = hasAnyPermission(user, ['product.create'])
  const canEdit = hasAnyPermission(user, ['product.edit'])
  // GET /products returns the full server-filtered result; category is a presentation filter.
  const rows = (products.data ?? []).filter(product => !categoryId || product.categoryId === categoryId)
  const currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1))
  const visible = rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
  const filtered = Boolean(search.trim() || itemType || active !== 'true' || categoryId)
  const pick = (event: React.MouseEvent, product: Product, edit = false) => { if (split && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); selectProduct(product.id, edit) } }
  const actions = (product: Product) => <Stack direction="row" spacing={0.5}>
    <Tooltip title="Переглянути"><IconButton component={Link} to={`/products/${product.id}`} size="small" onClick={event => pick(event, product)} aria-label={`Переглянути ${product.name}`}><Icon name="eye" sx={{ fontSize: 18 }} /></IconButton></Tooltip>
    {canEdit && <Tooltip title="Редагувати"><IconButton component={Link} to={`/products/${product.id}`} state={{ edit: true }} size="small" onClick={event => pick(event, product, true)} aria-label={`Редагувати ${product.name}`}><Icon name="edit" sx={{ fontSize: 18 }} /></IconButton></Tooltip>}
  </Stack>
  const columns: DataColumn<Product>[] = [
    { key: 'code', label: 'Код', width: 110, render: product => <Box title={product.code} sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{product.code}</Box> },
    { key: 'name', label: 'Назва', width: 200, render: product => <MuiLink component={Link} to={`/products/${product.id}`} onClick={event => pick(event, product)} title={product.name} sx={{ fontWeight: 600, display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{product.name}</MuiLink> },
    { key: 'type', label: 'Тип', width: 84, render: product => <TypeBadge type={product.itemType} /> },
    { key: 'category', label: 'Категорія', width: 100, render: product => <Box title={product.categoryId ? categoryName(product.categoryId) : undefined} sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{product.categoryId ? categoryName(product.categoryId) : '—'}</Box> },
    { key: 'unit', label: 'Одиниця', width: 90, render: product => <Box title={unitName(product.unitId, units.data ?? [])} sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{unitName(product.unitId, units.data ?? [])}</Box> },
    { key: 'price', label: 'Ціна', width: 90, render: product => <Typography variant="caption" color="text.secondary">{product.itemType === 'GOODS' ? 'За партіями' : 'За типом ціни'}</Typography> },
    ...(!split ? [{ key: 'barcode', label: 'Штрихкод', render: (product: Product) => product.barcode || '—' }] : []),
    { key: 'active', label: 'Статус', width: 96, render: product => <ActiveBadge active={product.active} /> },
    { key: 'actions', label: 'Дії', width: 88, render: actions },
  ]
  const content = <Stack spacing={2}>
    <PageHeader title="Товари" description="Номенклатура товарів і послуг" actions={canCreate && <Button component={Link} to="/products/new" variant="contained" startIcon={<Icon name="plus" />}>Додати</Button>} />
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <FilterBar mobileSheet>
        <TextField label="Пошук" placeholder="Назва, код або штрихкод" value={search} onChange={event => { setSearch(event.target.value); setPage(0) }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><Icon name="search" sx={{ fontSize: 18 }} /></InputAdornment> } }} sx={{ flex: '1 1 260px' }} />
        <FormControl size="small" sx={{ minWidth: { xs: 0, sm: 160 }, flex: { xs: '1 1 140px', sm: '0 0 160px' } }}><InputLabel id="product-type-filter" shrink>Тип</InputLabel><Select labelId="product-type-filter" label="Тип" displayEmpty notched value={itemType} onChange={event => { setItemType(event.target.value as ProductFilters['itemType']); setPage(0) }}><MenuItem value="">Усі</MenuItem><MenuItem value="GOODS">Товари</MenuItem><MenuItem value="SERVICE">Послуги</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: { xs: 0, sm: 160 }, flex: { xs: '1 1 140px', sm: '0 0 160px' } }}><InputLabel id="product-active-filter" shrink>Статус</InputLabel><Select labelId="product-active-filter" label="Статус" displayEmpty notched value={active} onChange={event => { setActive(event.target.value as ProductFilters['active']); setPage(0) }}><MenuItem value="true">Активні</MenuItem><MenuItem value="false">Неактивні</MenuItem><MenuItem value="">Усі</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ flex: '1 1 180px' }}><InputLabel id="product-category-filter" shrink>Категорія</InputLabel><Select labelId="product-category-filter" label="Категорія" value={categoryId} displayEmpty notched onChange={event => { setCategoryId(event.target.value); setPage(0) }}><MenuItem value="">Усі категорії</MenuItem>{directoryTree(categories.data ?? []).map(item => <MenuItem key={item.node.id} value={item.node.id}>{item.path}</MenuItem>)}</Select></FormControl>
      </FilterBar>
      {categories.isError && <RequestErrorState error={categories.error} retry={() => { void categories.refetch() }} />}
      {units.isError && <Alert severity="warning" action={<Button color="inherit" onClick={() => { void units.refetch() }}>Повторити</Button>} sx={{ mx: 2.5, mb: 2 }}>Не вдалося завантажити одиниці виміру.</Alert>}
      {products.isFetching && !products.isPending && <LinearProgress aria-label="Оновлення товарів" />}
      {products.isPending ? <ListLoading label="Завантаження товарів" /> : products.isError ? <RequestErrorState error={products.error} retry={() => { void products.refetch() }} /> : !rows.length ? <EmptyState icon="box" title={filtered ? 'Нічого не знайдено' : 'Товарів ще немає'} description={filtered ? 'Змініть пошук або фільтри.' : 'Додайте перший товар або послугу до номенклатури.'} action={filtered ? <Button onClick={() => { setSearch(''); setItemType(''); setActive('true'); setCategoryId(''); setPage(0) }}>Скинути фільтри</Button> : canCreate ? <Button component={Link} to="/products/new" variant="outlined" startIcon={<Icon name="plus" />}>Додати товар</Button> : undefined} /> : <>
        {mobile ? <Box component="ul" aria-label="Список товарів" sx={{ listStyle: 'none', p: 0, m: 0 }}>{visible.map(product => <MobileEntityCard key={product.id} title={<MuiLink component={Link} to={`/products/${product.id}`} sx={{ fontWeight: 600, overflowWrap: 'anywhere', minHeight: 44, display: 'inline-flex', alignItems: 'center' }}>{product.name}</MuiLink>} status={<ActiveBadge active={product.active} />}><Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>Код: {product.code}</Typography><Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}><TypeBadge type={product.itemType} /><Typography variant="caption" color="text.secondary">{unitName(product.unitId, units.data ?? [])}</Typography>{actions(product)}</Stack></MobileEntityCard>)}</Box> : <DataTable compact label="Товари" minWidth={880} rows={visible} columns={columns} rowId={product => product.id} selectedId={selected?.id} select={split ? product => selectProduct(product.id) : undefined} />}
        <Divider /><TablePagination component="div" count={rows.length} page={currentPage} rowsPerPage={pageSize} rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, value) => setPage(value)} onRowsPerPageChange={event => { setPageSize(Number(event.target.value)); setPage(0) }} labelRowsPerPage="На сторінці:" labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`} getItemAriaLabel={type => type === 'next' ? 'Наступна сторінка' : type === 'previous' ? 'Попередня сторінка' : type === 'first' ? 'Перша сторінка' : 'Остання сторінка'} sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end' }, '.MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } } }} />
      </>}
    </Paper>
  </Stack>
  return <><SplitViewLayout split={split} listWeight={1.05} detailLabel="Вибрана номенклатура" list={content} detail={selected ? <ProductDetail key={`${selected.id}-${selected.edit}`} id={selected.id} initialEdit={selected.edit} embedded onDirtyChange={dirtyChanged} /> : <Paper variant="outlined"><EmptyState icon="box" title="Оберіть товар або послугу" description="Дані, ціни та залишки вибраної номенклатури з’являться поруч зі списком." /></Paper>} /><ConfirmDialog open={pending !== null} title="Незбережені зміни" action="Відкинути зміни" danger close={() => setPending(null)} confirm={() => { setSelected(pending); setPending(null); setDirty(false) }}>Відкинути незбережені зміни й відкрити іншу номенклатуру?</ConfirmDialog></>
}
