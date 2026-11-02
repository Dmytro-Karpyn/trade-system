import { useState } from 'react'
import { Box, Button, Stack } from '@mui/material'
import { Link, useNavigate } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { partyReadPermissions } from './contracts'
import { CounterpartyForm } from './CounterpartyForm'

export function CounterpartyNewPage() {
  const user = useCurrentUser().data
  const navigate = useNavigate()
  const [formKey, setFormKey] = useState(0)
  if (!user) return null
  const back = hasAnyPermission(user, partyReadPermissions) ? '/counterparties' : '/'
  return <Stack spacing={2} sx={{ maxWidth: 1100 }}><Box><Button component={Link} to={back} startIcon={<Icon name="back" />} color="inherit" sx={{ p: 0, mb: 1.5 }}>Назад</Button><PageHeader title="Новий контрагент" description="Контактні дані, ролі та налаштування взаємодії" /></Box><CounterpartyForm key={formKey} cancel={() => { void navigate(back) }} saved={party => { if (hasAnyPermission(user, partyReadPermissions)) void navigate(`/counterparties/${party.id}`, { replace: true }); else setFormKey(value => value + 1) }} /></Stack>
}
