import { Alert, Paper, Typography } from '@mui/material'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { MoneyAmount } from '../../components/MoneyAmount'
import { useCustomerBalance } from './queries'

export function CustomerBalance({ id }: { id: string }) {
  const query = useCustomerBalance(id, true)
  if (!query.permitted) return <Alert severity="info">Баланс покупця доступний за правом перегляду взаєморозрахунків.</Alert>
  return <Paper variant="outlined" sx={{ p: 2.5 }}><Typography variant="subtitle1">Поточний баланс покупця · UAH</Typography>{query.isPending ? <ListLoading rows={1} label="Завантаження балансу покупця" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : <Typography sx={{ mt: 1 }}><MoneyAmount amount={query.data.find(balance => balance.currencyId === '00000000-0000-0000-0000-000000000980')?.balance ?? 0} currency="UAH" /></Typography>}</Paper>
}
