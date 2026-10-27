import { useState } from 'react'
import { Box, Button, Stack, Typography } from '@mui/material'
import { Link, useNavigate } from 'react-router-dom'
import { useCurrentUser } from '../../auth/queries'
import { hasAnyPermission } from '../../auth/contracts'
import { Icon } from '../../components/Icon'
import { PurchaseForm } from './PurchaseForm'

export function PurchaseNewPage() {
  const user = useCurrentUser().data
  const navigate = useNavigate()
  const [formKey, setFormKey] = useState(0)
  const canRead = Boolean(user && hasAnyPermission(user, ['purchase.view']))
  return <Stack spacing={3}><Box><Button component={Link} to={canRead ? '/purchases' : '/'} color="inherit" startIcon={<Icon name="back" />} sx={{ p: 0, mb: 1.5 }}>Назад</Button><Typography variant="h1" component="h1">Нова закупівля</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Прихідна накладна постачальника</Typography></Box><PurchaseForm key={formKey} saved={purchase => { if (canRead) void navigate(`/purchases/${purchase.document.id}`, { replace: true }); else setFormKey(value => value + 1) }} /></Stack>
}
