import { useState } from 'react'
import { Box, Button, Paper, Stack, Typography } from '@mui/material'
import { Link, useLocation, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { RequestErrorState } from '../../components/RequestState'
import { ActiveBadge } from '../../components/ActiveBadge'
import { TypeBadge } from './ProductBadges'
import { ProductForm } from './ProductForm'
import { ListLoading } from '../../components/ListLoading'
import { unitName } from './contracts'
import { useProduct, useUnits } from './queries'

function ProductDetail({ id }: { id: string }) {
  const user = useCurrentUser().data
  const location = useLocation()
  const [editing, setEditing] = useState(location.state?.edit === true)
  const query = useProduct(id)
  const units = useUnits()
  if (!user) return null
  const canEdit = hasAnyPermission(user, ['product.edit'])
  if (query.isPending) return <ListLoading label="Завантаження товарів" />
  if (query.isError) {
    if (query.error instanceof ApiError && query.error.status === 404) return <EmptyState icon="box" title="Товар не знайдено" description="Поверніться до списку та виберіть іншу позицію." action={<Button component={Link} to="/products">До товарів</Button>} />
    return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  }
  const product = query.data
  const fields = [
    ['Код', product.code], ['Назва', product.name], ['Категорія', product.categoryId ? 'Назву не вказано' : 'Не вибрано'],
    ['Одиниця виміру', unitName(product.unitId, units.data ?? [])], ['Штрихкод', product.barcode || '—'], ['Опис', product.description || '—'],
  ]
  return <Stack spacing={3} sx={{ maxWidth: 1100 }}>
    <Box><Button component={Link} to="/products" startIcon={<Icon name="back" />} color="inherit" sx={{ p: 0, mb: 1.5 }}>До товарів</Button><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}><Box sx={{ minWidth: 0 }}><Typography variant="h1" component="h1" sx={{ overflowWrap: 'anywhere' }}>{product.name}</Typography><Stack direction="row" spacing={1} sx={{ mt: 1.5 }}><TypeBadge type={product.itemType} /><ActiveBadge active={product.active} /></Stack></Box>{canEdit && !editing && <Button variant="contained" startIcon={<Icon name="edit" />} onClick={() => setEditing(true)}>Редагувати</Button>}</Stack></Box>
    {editing && canEdit ? <ProductForm key={product.id} product={product} cancel={() => setEditing(false)} saved={() => setEditing(false)} /> : <Paper variant="outlined" sx={{ p: { xs: 2.5, md: 3 } }}>
      <Typography variant="h2" component="h2" sx={{ mb: 3 }}>Основні дані</Typography>
      <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '160px 1fr' }, columnGap: 4, rowGap: 2 }}>{fields.map(([label, value]) => <Box key={label} sx={{ display: 'contents' }}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" variant="body1" sx={{ m: 0, overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{value}</Typography></Box>)}</Box>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>{product.itemType === 'SERVICE' ? 'Послуга не має складських залишків і партій' : 'Товар підтримує партії та складський облік'}</Typography>
      {units.isError && <Button onClick={() => { void units.refetch() }} size="small" sx={{ mt: 2 }}>Повторити завантаження одиниць</Button>}
    </Paper>}
  </Stack>
}
export function ProductDetailPage() {
  const { id = '' } = useParams()
  return <ProductDetail key={id} id={id} />
}
