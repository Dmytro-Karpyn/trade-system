import { useState } from 'react'
import { Alert, Box, Button, Paper, Stack, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { useCurrentUser } from '../../auth/queries'
import { hasAnyPermission } from '../../auth/contracts'
import { EmptyState } from '../../components/EmptyState'
import { FilterBar } from '../../components/FilterBar'
import { Icon } from '../../components/Icon'
import { KpiCard } from '../../components/KpiCard'
import { ListLoading } from '../../components/ListLoading'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName, money } from '../purchases/contracts'
import { periodDates } from './contracts'
import { metricText } from './format'
import { PeriodControls } from './PeriodControls'
import { ReportDataTable, ReportNotes } from './ReportTable'
import { TrendChart } from './TrendChart'
import { visibleDashboard } from './visibility'
import { useDashboard } from './queries'

const quickActions = [
  { title: 'Новий продаж', path: '/sales/new', permission: 'sale.create', icon: 'sale' },
  { title: 'Нова закупівля', path: '/purchases/new', permission: 'purchase.create', icon: 'purchase' },
  { title: 'Нова витрата', path: '/expenses/new', permission: 'expense.create', icon: 'expense' },
  { title: 'Надходження грошей', path: '/money/receipts/new', permission: 'money.receipt', icon: 'wallet' },
] as const

export function Dashboard() {
  const user = useCurrentUser().data
  const [period, setPeriod] = useState(() => periodDates('month'))
  const query = useDashboard(period)
  const data = query.data && user ? visibleDashboard(query.data, user) : undefined
  const currencies = [...new Set([
    ...(data?.kpis.flatMap(metric => metric.currencyId ? [metric.currencyId] : []) ?? []),
    ...(data?.blocks.flatMap(block => block.rows.flatMap(row => typeof row.currencyId === 'string' ? [row.currencyId] : [])) ?? []),
  ])].sort()
  const actions = quickActions.filter(action => user && hasAnyPermission(user, [action.permission]))
  const salesStructure = data?.kpis.filter(metric => ['sales', 'returns', 'netSales'].includes(metric.key)) ?? []
  return <Stack spacing={2.5}>
    <PageHeader title="Огляд діяльності" description="Робочий простір · показники за період та поточні залишки" actions={user && hasAnyPermission(user, ['reports.view']) && <Button component={Link} to="/reports" variant="outlined">Відкрити звіти</Button>} />
    <Paper variant="outlined"><FilterBar><PeriodControls compact value={period} change={setPeriod} /></FilterBar></Paper>
    {period.from > period.to ? <Alert severity="warning">Дата «від» не може бути пізніше дати «до».</Alert> : query.isPending ? <ListLoading label="Завантаження показників" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : data && <>
      <Box aria-label="Підсумки" sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))', lg: 'repeat(6, minmax(0, 1fr))' }, gap: 1.5 }}>{data.kpis.map((metric, index) => <KpiCard key={`${metric.key}-${metric.currencyId}-${index}`} label={metric.label + (metric.currencyId ? ` · ${currencyName(metric.currencyId, currencies)}` : '')} value={metricText(metric, currencies)} testId={`metric-${metric.key}`} />)}</Box>
      {!data.kpis.length && <EmptyState title="Немає доступних показників" description="Показники відображаються відповідно до ваших прав." />}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
        {data.salesTrend.length > 0 && <TrendChart title="Динаміка продажів" rows={data.salesTrend} series={[{ key: 'sales', label: 'Продажі' }, { key: 'returns', label: 'Повернення' }, { key: 'netSales', label: 'Чисті продажі' }]} />}
        {data.profitTrend.length > 0 && <TrendChart title="Динаміка валового прибутку" rows={data.profitTrend} series={[{ key: 'grossProfit', label: 'Валовий прибуток' }]} />}
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
        {salesStructure.length > 0 && <Paper component="section" variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 1.5 }}>Структура продажів</Typography><Stack spacing={1}>{salesStructure.map(metric => <Stack direction="row" key={metric.key} sx={{ justifyContent: 'space-between', gap: 1, py: 0.5 }}><Typography variant="body2" color="text.secondary">{metric.label}</Typography><Typography variant="body2" sx={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{metricText(metric, currencies)}</Typography></Stack>)}</Stack><Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>Продажі, повернення та чисті продажі за вибраний період. Розподіл за товарами й послугами API не надає.</Typography></Paper>}
        {actions.length > 0 && <Paper component="section" variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 1.5 }}>Швидкі дії</Typography><Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 1 }}>{actions.map(action => <Button key={action.path} component={Link} to={action.path} variant="outlined" startIcon={<Icon name={action.icon} />} sx={{ justifyContent: 'flex-start', textAlign: 'left', minHeight: 44 }}>{action.title}</Button>)}</Box></Paper>}
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'repeat(2, minmax(0, 1fr))' }, gap: 2, minWidth: 0 }}>{[...data.blocks].sort((a, b) => Number(b.key === 'recent') - Number(a.key === 'recent')).map(block => <Stack component="section" key={block.key} spacing={1.25} sx={{ minWidth: 0, gridColumn: ['stock', 'recent'].includes(block.key) ? '1 / -1' : undefined }}><Typography variant="h2" component="h2">{block.title}</Typography>
        {block.key === 'expenseCategories' && block.rows.length > 0 ? <Paper variant="outlined" sx={{ p: 2 }}><Stack spacing={1.5}>{block.rows.map((row, index) => <Box key={index}><Stack direction="row" sx={{ justifyContent: 'space-between', gap: 1 }}><Typography variant="body2">{String(row.category)}</Typography><Typography variant="body2" sx={{ whiteSpace: 'nowrap' }}>{money(Number(row.amountUah))} UAH</Typography></Stack><Box sx={{ height: 6, bgcolor: 'background.default', borderRadius: 1, mt: 0.75 }}><Box sx={{ height: 6, width: `${Number(row.amountUah) / Math.max(...block.rows.map(item => Number(item.amountUah)), 1) * 100}%`, bgcolor: 'primary.main', borderRadius: 1 }} /></Box></Box>)}</Stack></Paper> : <ReportDataTable data={block} currencies={currencies} emptyTitle={block.key === 'recent' ? 'Документів за період немає' : 'Даних поки немає'} />}
        <ReportNotes notes={block.notes} />
      </Stack>)}</Box>
      {user && hasAnyPermission(user, ['stock.view']) && <Paper component="section" variant="outlined"><EmptyState icon="stock" title="Низькі залишки" description="Dashboard API поки не надає перелік товарів із низьким залишком. Поточні залишки доступні в розділі «Склад»." action={<Button component={Link} to="/stock" variant="outlined">Переглянути склад</Button>} /></Paper>}
      <ReportNotes notes={data.notes} />
    </>}
  </Stack>
}
