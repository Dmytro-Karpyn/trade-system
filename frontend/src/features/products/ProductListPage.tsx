import { useState } from 'react'
import { Alert, Box, Button, Divider, FormControl, IconButton, InputAdornment, InputLabel, LinearProgress, Link as MuiLink, MenuItem, Paper, Select, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, TextField, Tooltip, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { useCurrentUser } from '../../auth/queries'
import { hasAnyPermission } from '../../auth/contracts'
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
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const debouncedSearch = useDebouncedValue(search)
  const products = useProducts({ search: debouncedSearch, itemType, active })
  const units = useUnits()
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (!user) return null
  const canCreate = hasAnyPermission(user, ['product.create'])
  const canEdit = hasAnyPermission(user, ['product.edit'])
  const rows = products.data ?? []
  const currentPage = Math.min(page, Math.max(0, Math.ceil(rows.length / pageSize) - 1))
  const visible = rows.slice(currentPage * pageSize, (currentPage + 1) * pageSize)
  const filtered = Boolean(search.trim() || itemType || active !== 'true')
  const actions = (product: Product) => <Stack direction="row" spacing={0.5}>
    <Tooltip title="Переглянути"><IconButton component={Link} to={`/products/${product.id}`} size="small" aria-label={`Переглянути ${product.name}`}><Icon name="eye" sx={{ fontSize: 18 }} /></IconButton></Tooltip>
    {canEdit && <Tooltip title="Редагувати"><IconButton component={Link} to={`/products/${product.id}`} state={{ edit: true }} size="small" aria-label={`Редагувати ${product.name}`}><Icon name="edit" sx={{ fontSize: 18 }} /></IconButton></Tooltip>}
  </Stack>
  return <Stack spacing={3}>
    <Stack direction="row" spacing={2} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
      <Box><Typography variant="h1" component="h1">Товари</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Номенклатура товарів і послуг</Typography></Box>
      {canCreate && <Button component={Link} to="/products/new" variant="contained" startIcon={<Icon name="plus" />}>Додати</Button>}
    </Stack>
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <Box sx={{ p: 2.5, display: 'flex', flexWrap: 'wrap', gap: 2 }}>
        <TextField label="Пошук" placeholder="Назва, код або штрихкод" value={search} onChange={event => { setSearch(event.target.value); setPage(0) }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><Icon name="search" sx={{ fontSize: 18 }} /></InputAdornment> } }} sx={{ flex: '1 1 260px' }} />
        <FormControl size="small" sx={{ minWidth: { xs: 0, sm: 160 }, flex: { xs: '1 1 140px', sm: '0 0 160px' } }}><InputLabel id="product-type-filter" shrink>Тип</InputLabel><Select labelId="product-type-filter" label="Тип" displayEmpty notched value={itemType} onChange={event => { setItemType(event.target.value as ProductFilters['itemType']); setPage(0) }}><MenuItem value="">Усі</MenuItem><MenuItem value="GOODS">Товари</MenuItem><MenuItem value="SERVICE">Послуги</MenuItem></Select></FormControl>
        <FormControl size="small" sx={{ minWidth: { xs: 0, sm: 160 }, flex: { xs: '1 1 140px', sm: '0 0 160px' } }}><InputLabel id="product-active-filter" shrink>Статус</InputLabel><Select labelId="product-active-filter" label="Статус" displayEmpty notched value={active} onChange={event => { setActive(event.target.value as ProductFilters['active']); setPage(0) }}><MenuItem value="true">Активні</MenuItem><MenuItem value="false">Неактивні</MenuItem><MenuItem value="">Усі</MenuItem></Select></FormControl>
      </Box>
      {units.isError && <Alert severity="warning" action={<Button color="inherit" onClick={() => { void units.refetch() }}>Повторити</Button>} sx={{ mx: 2.5, mb: 2 }}>Не вдалося завантажити одиниці виміру.</Alert>}
      {products.isFetching && !products.isPending && <LinearProgress aria-label="Оновлення товарів" />}
      {products.isPending ? <ListLoading label="Завантаження товарів" /> : products.isError ? <RequestErrorState error={products.error} retry={() => { void products.refetch() }} /> : !rows.length ? <EmptyState icon="box" title={filtered ? 'Нічого не знайдено' : 'Товарів ще немає'} description={filtered ? 'Змініть пошук або фільтри.' : 'Додайте перший товар або послугу до номенклатури.'} action={filtered ? <Button onClick={() => { setSearch(''); setItemType(''); setActive('true'); setPage(0) }}>Скинути фільтри</Button> : canCreate ? <Button component={Link} to="/products/new" variant="outlined" startIcon={<Icon name="plus" />}>Додати товар</Button> : undefined} /> : <>
        {mobile ? <Box component="ul" aria-label="Список товарів" sx={{ listStyle: 'none', p: 0, m: 0 }}>{visible.map(product => <Box component="li" key={product.id} sx={{ p: 2.5, borderTop: '1px solid', borderColor: 'divider' }}>
          <Stack direction="row" sx={{ alignItems: 'flex-start', justifyContent: 'space-between', gap: 1 }}><MuiLink component={Link} to={`/products/${product.id}`} underline="hover" sx={{ fontWeight: 600, color: 'text.primary', overflowWrap: 'anywhere' }}>{product.name}</MuiLink>{actions(product)}</Stack>
          <Typography variant="body2" color="text.secondary" sx={{ my: 1, overflowWrap: 'anywhere' }}>Код: {product.code}</Typography>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}><TypeBadge type={product.itemType} /><ActiveBadge active={product.active} /><Typography variant="caption" color="text.secondary">{unitName(product.unitId, units.data ?? [])}</Typography></Stack>
        </Box>)}</Box> : <TableContainer><Table aria-label="Товари" sx={{ minWidth: 880 }}><TableHead><TableRow>{['Код', 'Назва', 'Тип', 'Категорія', 'Одиниця', 'Штрихкод', 'Активний', 'Дії'].map(label => <TableCell key={label}>{label}</TableCell>)}</TableRow></TableHead><TableBody>{visible.map(product => <TableRow key={product.id} hover>
          <TableCell sx={{ whiteSpace: 'nowrap', fontWeight: 500 }}>{product.code}</TableCell><TableCell sx={{ minWidth: 180 }}><MuiLink component={Link} to={`/products/${product.id}`} underline="hover" sx={{ fontWeight: 600, color: 'text.primary', overflowWrap: 'anywhere' }}>{product.name}</MuiLink></TableCell><TableCell><TypeBadge type={product.itemType} /></TableCell><TableCell sx={{ color: 'text.secondary' }}>{product.categoryId ? 'Назву не вказано' : '—'}</TableCell><TableCell>{unitName(product.unitId, units.data ?? [])}</TableCell><TableCell sx={{ color: 'text.secondary', overflowWrap: 'anywhere', maxWidth: 160 }}>{product.barcode || '—'}</TableCell><TableCell><ActiveBadge active={product.active} /></TableCell><TableCell>{actions(product)}</TableCell>
        </TableRow>)}</TableBody></Table></TableContainer>}
        <Divider /><TablePagination component="div" count={rows.length} page={currentPage} rowsPerPage={pageSize} rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, value) => setPage(value)} onRowsPerPageChange={event => { setPageSize(Number(event.target.value)); setPage(0) }} labelRowsPerPage="На сторінці:" labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`} getItemAriaLabel={type => type === 'next' ? 'Наступна сторінка' : type === 'previous' ? 'Попередня сторінка' : type === 'first' ? 'Перша сторінка' : 'Остання сторінка'} sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end' }, '.MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } } }} />
      </>}
    </Paper>
  </Stack>
}
