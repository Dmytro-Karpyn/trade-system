import { useState } from 'react'
import { Box, Button, Stack } from '@mui/material'
import { Link, useNavigate } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { ProductForm } from './ProductForm'

export function ProductNewPage() {
  const user = useCurrentUser().data
  const navigate = useNavigate()
  const [formKey, setFormKey] = useState(0)
  if (!user) return null
  const canView = hasAnyPermission(user, ['product.view'])
  const back = canView ? '/products' : '/'
  return <Stack spacing={2} sx={{ maxWidth: 1100 }}>
    <Box><Button component={Link} to={back} startIcon={<Icon name="back" />} color="inherit" sx={{ p: 0, mb: 1.5 }}>Назад до {canView ? 'товарів' : 'головної'}</Button><PageHeader title="Нова номенклатура" description="Додайте товар або послугу." /></Box>
    <ProductForm key={formKey} cancel={() => { void navigate(back) }} saved={product => { if (canView) void navigate(`/products/${product.id}`, { replace: true }); else setFormKey(key => key + 1) }} />
  </Stack>
}
