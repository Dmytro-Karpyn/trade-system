import { useQuery } from '@tanstack/react-query'
import { Box, Button, Chip, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { fetchHealth } from '../api/health'
import { useAppServices } from '../app/context'
import { hasAnyPermission } from '../auth/contracts'
import { useCurrentUser } from '../auth/queries'
import { EmptyState } from '../components/EmptyState'
import { Icon } from '../components/Icon'
import type { IconName } from '../components/Icon'

const metrics: { title: string; icon: IconName; permission: string; tone: string }[] = [
  { title: 'Продажі', icon: 'sale', permission: 'sale.view', tone: '#edf3ff' },
  { title: 'Валовий прибуток', icon: 'trend', permission: 'profit.view', tone: '#eaf7f2' },
  { title: 'Витрати', icon: 'expense', permission: 'expense.view', tone: '#fff3eb' },
  { title: 'Управлінський результат', icon: 'chart', permission: 'profit.view', tone: '#f2effb' },
  { title: 'Нам винні', icon: 'downLeft', permission: 'settlement.view', tone: '#edf3ff' },
  { title: 'Ми винні', icon: 'upRight', permission: 'settlement.view', tone: '#fff3eb' },
  { title: 'Гроші', icon: 'wallet', permission: 'money.view', tone: '#eaf7f2' },
]

export function DashboardPage() {
  const user = useCurrentUser().data
  const { api } = useAppServices()
  const health = useQuery({ queryKey: ['health'], queryFn: ({ signal }) => fetchHealth(api, signal), refetchInterval: 30_000 })
  const connected = health.isSuccess && health.data.status === 'Healthy'
  const date = new Intl.DateTimeFormat('uk-UA', { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date())
  if (!user) return null
  return <Stack spacing={3.5}>
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { xs: 'flex-start', sm: 'center' }, justifyContent: 'space-between' }}>
      <Box><Typography variant="overline" color="text.secondary">РОБОЧИЙ ПРОСТІР</Typography><Typography variant="h1" component="h1" sx={{ mt: 0.5, mb: 1 }}>Огляд діяльності</Typography><Typography color="text.secondary" variant="body2">Ключові показники та останні операції вашої організації.</Typography></Box>
      <Chip icon={<Icon name="calendar" sx={{ fontSize: '16px !important' }} />} label={date} variant="outlined" sx={{ bgcolor: '#fff', height: 36, px: 0.5, color: 'text.secondary' }} />
    </Stack>
    <Box aria-label="Ключові показники" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', md: 'repeat(3, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }}>
      {metrics.map(metric => {
        const permitted = hasAnyPermission(user, [metric.permission])
        return <Paper component="section" variant="outlined" key={metric.title} sx={{ p: 2.5 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between' }}><Typography component="h2" variant="body2" sx={{ fontWeight: 550 }}>{metric.title}</Typography><Box sx={{ width: 32, height: 32, borderRadius: 1, bgcolor: metric.tone, color: '#607590', display: 'grid', placeItems: 'center', flexShrink: 0 }}><Icon name={permitted ? metric.icon : 'lock'} sx={{ fontSize: 18 }} /></Box></Stack>
          <Typography aria-label={`${metric.title}: показник недоступний`} sx={{ fontSize: 31, fontWeight: 600, color: '#c1cad7', lineHeight: 1.6, mt: 0.75 }}>—</Typography>
          <Typography variant="caption" color="text.secondary">{permitted ? 'Показник поки недоступний' : 'Доступ обмежено'}</Typography>
        </Paper>
      })}
      <Paper component="section" variant="outlined" sx={{ p: 2.5, bgcolor: '#fafcff' }}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}><Typography component="h2" variant="body2" sx={{ fontWeight: 550 }}>Стан системи</Typography><Icon name="shield" sx={{ fontSize: 20, color: connected ? 'success.main' : 'text.secondary' }} /></Stack>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 2, mb: 1 }}><Box sx={{ width: 7, height: 7, borderRadius: '50%', bgcolor: health.isPending ? 'text.disabled' : connected ? 'success.main' : 'error.main' }} /><Typography variant="body2" sx={{ fontWeight: 600 }}>{health.isPending ? 'Перевіряємо…' : connected ? 'З’єднання встановлено' : 'Немає з’єднання'}</Typography></Stack>
        <Button size="small" color="inherit" startIcon={<Icon name="refresh" sx={{ fontSize: '14px !important' }} />} onClick={() => { void health.refetch() }} disabled={health.isFetching} sx={{ minHeight: 26, p: 0, color: 'text.secondary', fontSize: 11 }}>Перевірити</Button>
      </Paper>
    </Box>
    <Paper component="section" variant="outlined" sx={{ overflow: 'hidden' }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', px: 3, py: 2.5, gap: 2 }}><Box><Typography component="h2" variant="h2" sx={{ mb: 0.5 }}>Останні операції</Typography><Typography variant="body2" color="text.secondary">Документи та зміни у вашому робочому просторі</Typography></Box><Chip label="Незабаром" size="small" sx={{ bgcolor: 'background.default', color: 'text.secondary' }} /></Stack>
      <TableContainer><Table aria-label="Останні операції" sx={{ minWidth: 600 }}><TableHead><TableRow>{['Документ', 'Контрагент', 'Дата', 'Статус', 'Сума'].map(label => <TableCell key={label} align={label === 'Сума' ? 'right' : 'left'}>{label}</TableCell>)}</TableRow></TableHead><TableBody /></Table></TableContainer>
      <EmptyState title="Операції поки не відображаються" description="Стрічка останніх операцій буде доступна на наступному етапі." />
    </Paper>
    <Typography variant="caption" color="text.secondary">Показники та стрічка операцій з’являться після підключення відповідних розділів.</Typography>
  </Stack>
}
