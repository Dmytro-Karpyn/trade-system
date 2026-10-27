import { useState } from 'react'
import { Box, Button, Chip, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { Link, useLocation, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ActiveBadge } from '../../components/ActiveBadge'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { PartyBadges } from './CounterpartyBadges'
import { CounterpartyForm } from './CounterpartyForm'
import { legalTypes, priceTypeName } from './contracts'
import { useCounterparty, usePriceTypes } from './queries'
import { SettlementsTab } from './SettlementsTab'

const tabs = ['Загальне', 'Взаєморозрахунки', 'Продажі', 'Закупівлі', 'Витрати', 'Гроші']
function CounterpartyDetail({ id }: { id: string }) {
  const user = useCurrentUser().data
  const location = useLocation()
  const [editing, setEditing] = useState(location.state?.edit === true)
  const [tab, setTab] = useState(0)
  const query = useCounterparty(id)
  const prices = usePriceTypes()
  if (!user) return null
  if (query.isPending) return <ListLoading label="Завантаження контрагента" />
  if (query.isError) {
    if (query.error instanceof ApiError && query.error.status === 404) return <EmptyState icon="people" title="Контрагента не знайдено" action={<Button component={Link} to="/counterparties">До контрагентів</Button>} />
    return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  }
  const party = query.data
  const canWrite = hasAnyPermission(user, ['settings.manage'])
  const canSettle = hasAnyPermission(user, ['settlement.view'])
  const fields = [['Назва', party.name], ['Тип', party.legalType ? legalTypes[party.legalType] : 'Не вказано'], ['ІПН', party.taxId || '—'], ['Реєстраційний код', party.registrationCode || '—'], ['Телефон', party.phone || '—'], ['Email', party.email || '—'], ['Адреса', party.address || '—'], ['Тип ціни за замовчуванням', priceTypeName(party.defaultPriceTypeId, prices.data ?? [])], ['Примітка', party.note || '—']]
  return <Stack spacing={3}>
    <Box><Button component={Link} to="/counterparties" startIcon={<Icon name="back" />} color="inherit" sx={{ p: 0, mb: 1.5 }}>До контрагентів</Button><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}><Box sx={{ minWidth: 0 }}><Typography variant="h1" component="h1" sx={{ overflowWrap: 'anywhere' }}>{party.name}</Typography><Stack direction="row" spacing={1} sx={{ mt: 1.5, alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>{party.legalType && <Chip label={legalTypes[party.legalType]} size="small" variant="outlined" />}<PartyBadges party={party} /><ActiveBadge active={party.active} /></Stack></Box>{canWrite && !(tab === 0 && editing) && <Button variant="contained" startIcon={<Icon name="edit" />} onClick={() => { setTab(0); setEditing(true) }}>Редагувати</Button>}</Stack></Box>
    <Box sx={{ borderBottom: '1px solid', borderColor: 'divider', minWidth: 0 }}><Tabs value={tab} onChange={(_, value: number) => setTab(value)} variant="scrollable" scrollButtons="auto" allowScrollButtonsMobile aria-label="Розділи контрагента">{tabs.map((label, index) => <Tab key={label} id={`party-tab-${index}`} aria-controls={`party-panel-${index}`} label={label} disabled={index === 1 && !canSettle} title={index === 1 && !canSettle ? 'Потрібне право перегляду взаєморозрахунків' : undefined} />)}</Tabs></Box>
    <Box role="tabpanel" id={`party-panel-${tab}`} aria-labelledby={`party-tab-${tab}`}>
      {tab === 0 ? editing && canWrite ? <CounterpartyForm key={party.id} party={party} saved={() => setEditing(false)} cancel={() => setEditing(false)} /> : <Paper variant="outlined" sx={{ p: { xs: 2.5, md: 3 } }}><Typography variant="h2" component="h2" sx={{ mb: 3 }}>Основні дані</Typography><Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '180px 1fr' }, columnGap: 4, rowGap: 2 }}>{fields.map(([label, value]) => <Box key={label} sx={{ display: 'contents' }}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" variant="body1" sx={{ m: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{value}</Typography></Box>)}</Box>{prices.permitted && prices.isError && <Button size="small" sx={{ mt: 2 }} onClick={() => { void prices.refetch() }}>Повторити завантаження типів цін</Button>}</Paper> : tab === 1 && canSettle ? <SettlementsTab id={id} /> : <Paper variant="outlined"><EmptyState icon={tab === 2 ? 'sale' : tab === 3 ? 'purchase' : tab === 4 ? 'expense' : 'wallet'} title="Розділ буде реалізовано наступним етапом" /></Paper>}
    </Box>
  </Stack>
}
export function CounterpartyDetailPage() {
  const { id = '' } = useParams()
  return <CounterpartyDetail key={id} id={id} />
}
