import { Box, Paper, Typography } from '@mui/material'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { money } from '../purchases/contracts'
import { useSaleCost, useSaleProfit } from './queries'

export function SaleProfit({ id }: { id: string }) {
  const user = useCurrentUser().data
  const canCost = Boolean(user && hasAnyPermission(user, ['cost.view'])); const canProfit = Boolean(user && hasAnyPermission(user, ['profit.view']))
  const cost = useSaleCost(id, canCost); const profit = useSaleProfit(id, canProfit)
  if (!canCost && !canProfit) return null
  const values: [string, string][] = []
  if (canCost && cost.data) values.push(['Поточна собівартість', `${money(cost.data.currentCost)} UAH`])
  if (canProfit && profit.data) values.push(['Чистий продаж', `${money(profit.data.netSale)} UAH`], ['Валовий прибуток', `${money(profit.data.grossProfit)} UAH`], ['Маржа', profit.data.margin === null ? 'Не визначається для нульової суми' : `${money(profit.data.margin)}%`])
  return <Paper variant="outlined" sx={{ p: 2.5 }}><Typography variant="h2" component="h2">Поточні показники продажу</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>За поточною собівартістю партій</Typography>{(canCost && cost.isPending || canProfit && profit.isPending) && <ListLoading rows={1} label="Завантаження показників продажу" />}{(canCost && cost.isError || canProfit && profit.isError) && <RequestErrorState error={cost.error ?? profit.error} retry={() => { if (canCost) void cost.refetch(); if (canProfit) void profit.refetch() }} />}<Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', xl: 'repeat(4, 1fr)' }, gap: 3, mt: 3 }}>{values.map(([label, value]) => <Box key={label}><Typography variant="body2" color="text.secondary">{label}</Typography><Typography variant="subtitle1" sx={{ mt: 1 }}>{value}</Typography></Box>)}</Box></Paper>
}
