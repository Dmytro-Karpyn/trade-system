import { useState } from 'react'
import { Box, Button, Link as MuiLink, MenuItem, Paper, Stack, Tab, Tabs, TextField, useMediaQuery, useTheme } from '@mui/material'
import { Link, useParams } from 'react-router-dom'
import { BalanceBadge } from '../../components/BalanceBadge'
import { DataTable } from '../../components/DataTable'
import { FilterBar } from '../../components/FilterBar'
import { Icon } from '../../components/Icon'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { MoneyAmount } from '../../components/MoneyAmount'
import { CurrencyAmount } from '../../components/CurrencyAmount'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { useCounterparty } from '../counterparties/queries'
import { SettlementsTab } from '../counterparties/SettlementsTab'
import { currencyName } from '../purchases/contracts'
import { useSettlementCurrencies, useSettlementOverview } from './queries'
import type { SettlementFilters } from './queries'

const states = [{ value: '', label: 'Усі' }, { value: 'OWED_TO_US', label: 'Нам винні' }, { value: 'WE_OWE', label: 'Ми винні' }, { value: 'ZERO', label: 'Нульові' }] as const
export function SettlementsPage() {
  const [filters, setFilters] = useState<SettlementFilters>({ search: '', state: '', currencyId: '' }), [selected, setSelected] = useState<string | null>(null)
  const [selectedRow, setSelectedRow] = useState<string | null>(null)
  const choose = (row: { counterpartyId: string; currencyId: string }) => { setSelected(row.counterpartyId); setSelectedRow(`${row.counterpartyId}-${row.currencyId}`) }
  const query = useSettlementOverview({ ...filters, search: useDebouncedValue(filters.search, 300) }), currencies = useSettlementCurrencies()
  const mobile = useMediaQuery(useTheme().breakpoints.down('md')), split = useMediaQuery(useTheme().breakpoints.up('lg'))
  const ids = currencies.data ?? query.data?.map(row => row.currencyId) ?? []
  const content = <Stack spacing={2}><PageHeader title="Взаєморозрахунки" description="+ = контрагент винен нам; − = ми винні контрагенту. Кожна валюта має окремий баланс." /><Paper variant="outlined">
    <FilterBar mobileSheet><TextField label="Пошук контрагента" value={filters.search} onChange={event => setFilters({ ...filters, search: event.target.value })} sx={{ flex: '1 1 200px' }} /><TextField label="Валюта" select value={filters.currencyId} onChange={event => setFilters({ ...filters, currencyId: event.target.value })} sx={{ flex: '1 1 150px' }}><MenuItem value="">Усі валюти</MenuItem>{ids.map(id => <MenuItem value={id} key={id}>{currencyName(id, ids)}</MenuItem>)}</TextField></FilterBar>
    <Tabs value={filters.state} onChange={(_, value: SettlementFilters['state']) => setFilters({ ...filters, state: value })} variant="scrollable" scrollButtons="auto" aria-label="Стан взаєморозрахунків">{states.map(state => <Tab value={state.value} label={state.label} key={state.value} />)}</Tabs>
    {query.isPending ? <ListLoading label="Завантаження балансів взаєморозрахунків" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : !query.data.length ? <EmptyState title="Балансів взаєморозрахунків немає" description="Немає проведених рухів або рядків за вибраними фільтрами." /> : mobile ? <Box component="ul" aria-label="Баланси взаєморозрахунків" sx={{ p: 0, m: 0, listStyle: 'none' }}>{query.data.map(row => <MobileEntityCard key={`${row.counterpartyId}-${row.currencyId}`} title={<MuiLink component={Link} to={`/settlements/${row.counterpartyId}`} sx={{ minHeight: 44, display: 'inline-flex', alignItems: 'center' }}>{row.counterpartyName}</MuiLink>} status={<BalanceBadge balance={row.balance} />} amount={<CurrencyAmount amount={row.balance} currency={currencyName(row.currencyId, ids)} />}><></></MobileEntityCard>)}</Box> : <DataTable label="Баланси взаєморозрахунків" rows={query.data} rowId={row => `${row.counterpartyId}-${row.currencyId}`} selectedId={selectedRow} select={split ? choose : undefined} columns={[
      { key: 'party', label: 'Контрагент', render: row => <MuiLink component={Link} to={`/settlements/${row.counterpartyId}`} onClick={event => { if (split && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); choose(row) } }}>{row.counterpartyName}</MuiLink> },
      { key: 'currency', label: 'Валюта', render: row => currencyName(row.currencyId, ids) }, { key: 'balance', label: 'Баланс', align: 'right', render: row => <MoneyAmount amount={row.balance} /> }, { key: 'state', label: 'Стан', render: row => <BalanceBadge balance={row.balance} /> },
    ]} />}
  </Paper>{currencies.isError && <RequestErrorState error={currencies.error} retry={() => { void currencies.refetch() }} />}</Stack>
  return <SplitViewLayout split={split} list={content} detailLabel="Вибраний контрагент" detail={selected ? <SettlementDetail key={selected} counterpartyId={selected} embedded /> : <Paper variant="outlined"><EmptyState icon="settlement" title="Оберіть контрагента" /></Paper>} />
}
export function SettlementDetailPage() { const { counterpartyId = '' } = useParams(); return <SettlementDetail counterpartyId={counterpartyId} /> }
export function SettlementDetail({ counterpartyId, embedded = false }: { counterpartyId: string; embedded?: boolean }) {
  const party = useCounterparty(counterpartyId), currencies = useSettlementCurrencies()
  if (party.isPending) return <ListLoading label="Завантаження контрагента" />
  if (party.isError) return <RequestErrorState error={party.error} retry={() => { void party.refetch() }} />
  return <Stack spacing={2}><Button component={Link} to={embedded ? `/settlements/${counterpartyId}` : '/settlements'} startIcon={<Icon name={embedded ? 'upRight' : 'back'} />} sx={{ alignSelf: 'flex-start' }}>{embedded ? 'Відкрити окремо' : 'До взаєморозрахунків'}</Button><PageHeader title={party.data.name} secondary={embedded} actions={<Button component={Link} to={`/counterparties/${counterpartyId}`}>Відкрити контрагента</Button>} /><SettlementsTab id={counterpartyId} compact={embedded} showDetailLink={false} currencyIds={currencies.data} /></Stack>
}
