import { useState } from 'react'
import { Box, Button, Paper, Stack, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ActiveBadge } from '../../components/ActiveBadge'
import { DataTable } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { FinancialSummaryCard } from '../../components/FinancialSummaryCard'
import { Icon } from '../../components/Icon'
import { KpiCard } from '../../components/KpiCard'
import { ListLoading } from '../../components/ListLoading'
import { MoneyAmount } from '../../components/MoneyAmount'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName } from '../purchases/contracts'
import { periodDates } from '../reports/contracts'
import { useDashboard } from '../reports/queries'
import { AccountDialog } from './AccountDialog'
import { accountTypes, moneyFilters, moneyKinds, moneyRights, moneySingular } from './contracts'
import type { AccountOverview } from './contracts'
import { useMoneyAccounts, useMoneyList, useMoneyLookups } from './queries'

function MoneyActions() {
  const user = useCurrentUser().data
  return <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>{moneyKinds.filter(kind => user && hasAnyPermission(user, [moneyRights[kind]])).map(kind => <Button component={Link} to={`/money/${kind}/new`} variant="outlined" key={kind}>{moneySingular[kind]}</Button>)}</Stack>
}
export function MoneyLandingPage() {
  const user = useCurrentUser().data
  return user && hasAnyPermission(user, ['money.view']) ? <MoneyDashboard /> : <Stack spacing={2}><PageHeader title="Гроші" description="Оберіть доступну грошову операцію. Перегляд рахунків і балансів потребує money.view." /><MoneyActions /></Stack>
}
function AccountRows({ rows, currencies, edit, cards = false }: { rows: AccountOverview[]; currencies: string[]; edit?: (row: AccountOverview) => void; cards?: boolean }) {
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (!rows.length) return <EmptyState title="Рахунків ще немає" description="Створіть грошовий рахунок, щоб оформлювати надходження, виплати й перекази." />
  return mobile || cards ? <Box component="ul" aria-label="Грошові рахунки" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', sm: 'repeat(2,minmax(0,1fr))', xl: 'repeat(3,minmax(0,1fr))' }, gap: 1.5 }}>{rows.map(row => <Box component="li" key={row.account.id}><FinancialSummaryCard label={row.account.name} amount={row.balance} currency={currencyName(row.account.currencyId, currencies)}><Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap' }}><Typography variant="caption" color="text.secondary">{accountTypes[row.account.accountType]}</Typography><ActiveBadge active={row.account.active} />{edit && <Button onClick={() => edit(row)} aria-label={`Редагувати рахунок ${row.account.name}`}>Редагувати</Button>}</Stack></FinancialSummaryCard></Box>)}</Box> : <DataTable label="Грошові рахунки" rows={rows} rowId={row => row.account.id} columns={[
    { key: 'name', label: 'Назва', render: row => row.account.name }, { key: 'type', label: 'Тип', render: row => accountTypes[row.account.accountType] }, { key: 'currency', label: 'Валюта', render: row => currencyName(row.account.currencyId, currencies) }, { key: 'balance', label: 'Баланс', align: 'right', render: row => <MoneyAmount amount={row.balance} /> }, { key: 'active', label: 'Активний', render: row => <ActiveBadge active={row.account.active} /> }, ...(edit ? [{ key: 'edit', label: 'Дії', render: (row: AccountOverview) => <Button size="small" onClick={() => edit(row)} aria-label={`Редагувати рахунок ${row.account.name}`}>Редагувати</Button> }] : []),
  ]} />
}
export function MoneyDashboard() {
  const query = useMoneyAccounts(), lookups = useMoneyLookups(), summary = useDashboard(periodDates('month'))
  const receipts = useMoneyList('receipts', { ...moneyFilters, status: 'POSTED' }), payments = useMoneyList('payments', { ...moneyFilters, status: 'POSTED' }), transfers = useMoneyList('transfers', { ...moneyFilters, status: 'POSTED' })
  const currencies = lookups.data?.currencyIds ?? query.data?.map(row => row.account.currencyId) ?? []
  const totalBalances = summary.data?.kpis.filter(metric => metric.key === 'money' && metric.currencyId) ?? []
  return <Stack spacing={2}>
    <PageHeader title="Гроші" description="Поточні баланси окремо за валютами. Від’ємний баланс допустимий." actions={<Button component={Link} to="/money/accounts" variant="contained">Рахунки</Button>} />
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2,minmax(0,1fr))', lg: 'repeat(4,minmax(0,1fr))' }, gap: 1.5 }}>
      {totalBalances.length ? totalBalances.map(metric => <FinancialSummaryCard key={metric.currencyId} label="Загальний баланс" amount={metric.value} currency={currencyName(metric.currencyId!, currencies)} />) : <KpiCard label="Загальний баланс" value={summary.isPending ? '…' : summary.isError ? 'Недоступно' : '—'} />}
      {([['Надходження', receipts], ['Виплати', payments], ['Перекази', transfers]] as const).map(([label, result]) => <KpiCard key={label} label={`${label} · проведених документів`} value={result.isPending ? '…' : result.isError ? 'Недоступно' : String(result.data.length)} />)}
    </Box>
    {(summary.isError || receipts.isError || payments.isError || transfers.isError) && <RequestErrorState error={summary.error ?? receipts.error ?? payments.error ?? transfers.error} retry={() => { void summary.refetch(); void receipts.refetch(); void payments.refetch(); void transfers.refetch() }} />}
    <MoneyActions /><Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap' }}>{moneyKinds.map(kind => <Button component={Link} to={`/money/${kind}`} key={kind}>{kind === 'receipts' ? 'Усі надходження' : kind === 'payments' ? 'Усі виплати' : 'Усі перекази'}</Button>)}</Stack>
    <Box component="section"><Typography variant="h2" component="h2" sx={{ mb: 1.5 }}>Поточні баланси рахунків</Typography>{query.isPending ? <ListLoading label="Завантаження грошових балансів" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : <AccountRows rows={query.data} currencies={currencies} cards />}</Box>
  </Stack>
}
export function MoneyAccountsPage() {
  const query = useMoneyAccounts(), lookups = useMoneyLookups(), user = useCurrentUser().data, canEdit = Boolean(user && hasAnyPermission(user, ['settings.manage']))
  const [dialog, setDialog] = useState<AccountOverview | 'new' | null>(null)
  return <Stack spacing={2}><Button component={Link} to="/money" startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>До грошей</Button><PageHeader title="Грошові рахунки" actions={canEdit && <Button variant="contained" startIcon={<Icon name="plus" />} onClick={() => setDialog('new')}>Додати рахунок</Button>} /><Paper variant="outlined">{query.isPending ? <ListLoading label="Завантаження грошових рахунків" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : <AccountRows rows={query.data} currencies={lookups.data?.currencyIds ?? query.data.map(row => row.account.currencyId)} edit={canEdit ? row => setDialog(row) : undefined} />}</Paper>{dialog && <AccountDialog account={dialog === 'new' ? undefined : dialog} close={() => setDialog(null)} />}</Stack>
}
