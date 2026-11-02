import { useState } from 'react'
import { Box, Button, Stack } from '@mui/material'
import { Link, useNavigate } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { SaleForm } from './SaleForm'
export function SaleNewPage() {
  const user = useCurrentUser().data; const navigate = useNavigate(); const [key, setKey] = useState(0)
  const canRead = Boolean(user && hasAnyPermission(user, ['sale.view']))
  return <Stack spacing={2}><Box><Button component={Link} to={canRead ? '/sales' : '/'} color="inherit" startIcon={<Icon name="back" />} sx={{ p: 0, mb: 1.5 }}>Назад</Button><PageHeader title="Новий продаж" description="Товари та послуги · UAH" /></Box><SaleForm key={key} saved={sale => { if (canRead) void navigate(`/sales/${sale.document.id}`, { replace: true }); else setKey(value => value + 1) }} /></Stack>
}
