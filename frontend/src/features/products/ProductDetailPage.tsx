import { StockProductDetail } from '../stock/StockPages'
import { ServicePrices } from './ServicePrices'
import { useCategories } from '../directories/queries'
import { directoryTree } from '../directories/contracts'
import { useState } from 'react'
import { Box, Button, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { Link, useLocation, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { RequestErrorState } from '../../components/RequestState'
import { ActiveBadge } from '../../components/ActiveBadge'
import { TypeBadge } from './ProductBadges'
import { ProductForm } from './ProductForm'
import { ListLoading } from '../../components/ListLoading'
import { unitName } from './contracts'
import { useProduct, useUnits } from './queries'

export function ProductDetail({ id, embedded = false, initialEdit = false, onDirtyChange }: { id: string; embedded?: boolean; initialEdit?: boolean; onDirtyChange?: (dirty: boolean) => void }) {
  const user = useCurrentUser().data
  const location = useLocation()
  const [editing, setEditing] = useState(initialEdit || location.state?.edit === true)
  const [tab, setTab] = useState('general')
  const query = useProduct(id)
  const units = useUnits(); const categories=useCategories(); const categoryName=(id:string)=>directoryTree(categories.data??[]).find(n=>n.node.id===id)?.path??'Назву не вказано'
  if (!user) return null
  const canEdit = hasAnyPermission(user, ['product.edit'])
  if (query.isPending) return <ListLoading label="Завантаження товарів" />
  if (query.isError) {
    if (query.error instanceof ApiError && query.error.status === 404) return <EmptyState icon="box" title="Товар не знайдено" description="Поверніться до списку та виберіть іншу позицію." action={<Button component={Link} to="/products">До товарів</Button>} />
    return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  }
  const product = query.data
  const fields = [
    ['Код', product.code], ['Назва', product.name], ['Категорія', product.categoryId ? categoryName(product.categoryId) : 'Не вибрано'],
    ['Одиниця виміру', unitName(product.unitId, units.data ?? [])], ['Штрихкод', product.barcode || '—'], ['Опис', product.description || '—'],
  ]
  const canStock = hasAnyPermission(user, ['stock.view'])
  return <Stack spacing={2} sx={{ maxWidth: 1100 }}>
    <Box><Button component={Link} to={embedded ? `/products/${id}` : '/products'} startIcon={<Icon name={embedded ? 'upRight' : 'back'} />} color="inherit" sx={{ p: 0, mb: 1.5 }}>{embedded ? 'Відкрити окремо' : 'До товарів'}</Button><PageHeader title={product.name} secondary={embedded} description={<Stack direction="row" spacing={1}><TypeBadge type={product.itemType} /><ActiveBadge active={product.active} /></Stack>} actions={canEdit && !editing && <Button variant="contained" startIcon={<Icon name="edit" />} onClick={() => { setTab('general'); setEditing(true) }}>Редагувати</Button>} /></Box>
    <Tabs value={tab} onChange={(_, value: string) => setTab(value)} variant="scrollable" aria-label="Дані номенклатури" sx={{ borderBottom: '1px solid', borderColor: 'divider' }}><Tab value="general" label="Загальне" id={`product-${id}-general-tab`} aria-controls={`product-${id}-general`} /><Tab value="prices" label="Ціни" id={`product-${id}-prices-tab`} aria-controls={`product-${id}-prices`} />{product.itemType === 'GOODS' && [<Tab key="stock" value="stock" label="Залишки" disabled={!canStock} id={`product-${id}-stock-tab`} aria-controls={`product-${id}-stock`} />, <Tab key="batches" value="batches" label="Партії" disabled={!canStock} id={`product-${id}-batches-tab`} aria-controls={`product-${id}-batches`} />]}</Tabs>
    <Box role="tabpanel" id={`product-${id}-general`} aria-labelledby={`product-${id}-general-tab`} hidden={tab !== 'general'}>
    {editing && canEdit ? <ProductForm key={product.id} onDirtyChange={onDirtyChange} product={product} cancel={() => { setEditing(false); onDirtyChange?.(false) }} saved={() => { setEditing(false); onDirtyChange?.(false) }} /> : <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h2" component="h2" sx={{ mb: 2 }}>Основні дані</Typography>
      <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '160px 1fr' }, columnGap: 2, rowGap: 1.5 }}>{fields.map(([label, value]) => <Box key={label} sx={{ display: 'contents' }}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" variant="body1" sx={{ m: 0, overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{value}</Typography></Box>)}</Box>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>{product.itemType === 'SERVICE' ? 'Послуга не має складських залишків і партій' : 'Товар підтримує партії та складський облік'}</Typography>
      {units.isError && <Button onClick={() => { void units.refetch() }} size="small" sx={{ mt: 2 }}>Повторити завантаження одиниць</Button>}
    </Paper>}
    </Box>
    {['general', 'prices'].includes(tab) && (product.itemType === 'SERVICE' ? <Box role={tab === 'prices' ? 'tabpanel' : undefined} id={`product-${id}-prices`} aria-labelledby={tab === 'prices' ? `product-${id}-prices-tab` : undefined}><ServicePrices compact={embedded} productId={product.id} /></Box> : <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="body2" color="text.secondary">{tab === 'prices' ? 'Ціни товару визначаються по партіях.' : 'Ціни товару встановлюються окремо для кожної партії.'}</Typography>{canStock && <Button onClick={() => setTab('batches')}>Переглянути ціни партій</Button>}</Paper>)}
    {product.itemType === 'GOODS' && canStock && ['stock', 'batches'].includes(tab) && <Box role="tabpanel" id={`product-${id}-${tab}`} aria-labelledby={`product-${id}-${tab}-tab`}><StockProductDetail key={tab} productId={id} embedded hideHeader hideAvailable fixedTab={tab === 'stock' ? 0 : 1} /></Box>}

  </Stack>
}
export function ProductDetailPage() {
  const { id = '' } = useParams()
  return <ProductDetail key={id} id={id} />
}
