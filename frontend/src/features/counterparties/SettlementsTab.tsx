import { Box, Chip, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography, useMediaQuery, useTheme } from '@mui/material'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { balanceStatus, formatAmount, operationNames, uahId } from './contracts'
import { usePartySettlements } from './queries'

export function SettlementsTab({ id }: { id: string }) {
  const query = usePartySettlements(id)
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (query.isPending) return <ListLoading label="Завантаження взаєморозрахунків" />
  if (query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const { balances, movements } = query.data
  // Currency directory is not implemented. Only the agreed UAH ID has a known label;
  // other currencies get distinct neutral labels, never an invented ISO code or a visible GUID.
  const unknownIds = [...new Set([...balances, ...movements].map(item => item.currencyId))].filter(currency => currency !== uahId).sort()
  const currencyName = (currency: string) => currency === uahId ? 'UAH' : `Валюта ${unknownIds.indexOf(currency) + 1}`
  const date = (value: string) => value.split('-').reverse().join('.')
  return <Stack spacing={3}>
    <Box><Typography variant="h2" component="h2">Баланси за валютами</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Взаєморозрахунки за проведеними документами</Typography></Box>
    {balances.length > 0 && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' }, gap: 2 }}>{balances.map(balance => <Paper component="section" aria-label={`Баланс ${currencyName(balance.currencyId)}`} variant="outlined" key={balance.currencyId} sx={{ p: 2.5 }}><Typography variant="subtitle1">{currencyName(balance.currencyId)}</Typography>{balance.currencyId !== uahId && <Typography variant="caption" color="text.secondary">Назву валюти не задано</Typography>}<Typography sx={{ fontSize: 26, fontWeight: 650, my: 1, overflowWrap: 'anywhere', fontVariantNumeric: 'tabular-nums' }}>{formatAmount(balance.balance)}</Typography><Chip size="small" label={balanceStatus(balance.balance)} variant="outlined" sx={{ color: balance.balance === 0 ? 'text.secondary' : balance.balance > 0 ? '#986523' : '#355eab' }} /></Paper>)}</Box>}
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <Box sx={{ p: 2.5 }}><Typography component="h2" variant="h2">Рухи взаєморозрахунків</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>У хронологічному порядку, баланс окремо для кожної валюти</Typography></Box>
      {!movements.length ? <EmptyState icon="settlement" title="Рухів взаєморозрахунків ще немає" description="Тут відображатимуться проведені операції з цим контрагентом." /> : mobile ? <Box component="ul" aria-label="Рухи взаєморозрахунків" sx={{ p: 0, m: 0, listStyle: 'none' }}>{movements.map(movement => <Box component="li" key={movement.id} sx={{ p: 2.5, borderTop: '1px solid', borderColor: 'divider' }}><Stack direction="row" sx={{ justifyContent: 'space-between', gap: 2 }}><Typography variant="subtitle1" sx={{ overflowWrap: 'anywhere' }}>{movement.documentNumber || 'Без номера'}</Typography><Typography variant="caption" color="text.secondary">{date(movement.documentDate)}</Typography></Stack><Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{operationNames[movement.documentType] ?? 'Інша операція'}</Typography><Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}><Box><Typography variant="caption" color="text.secondary">Рух · {currencyName(movement.currencyId)}</Typography><Typography variant="body2" sx={{ fontWeight: 600 }}>{formatAmount(movement.amount, true)}</Typography></Box><Box><Typography variant="caption" color="text.secondary">Баланс після операції</Typography><Typography variant="body2" sx={{ fontWeight: 600 }}>{formatAmount(movement.balanceAfterMovement)}</Typography></Box></Box></Box>)}</Box> : <TableContainer><Table aria-label="Рухи взаєморозрахунків" sx={{ minWidth: 760 }}><TableHead><TableRow><TableCell>Дата</TableCell><TableCell>Документ</TableCell><TableCell>Тип операції</TableCell><TableCell align="right">Рух</TableCell><TableCell align="right">Баланс після операції</TableCell></TableRow></TableHead><TableBody>{movements.map(movement => <TableRow key={movement.id} hover><TableCell sx={{ whiteSpace: 'nowrap' }}>{date(movement.documentDate)}</TableCell><TableCell sx={{ fontWeight: 550 }}>{movement.documentNumber || 'Без номера'}</TableCell><TableCell>{operationNames[movement.documentType] ?? 'Інша операція'}</TableCell><TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{formatAmount(movement.amount, true)}<Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>{currencyName(movement.currencyId)}</Typography></TableCell><TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>{formatAmount(movement.balanceAfterMovement)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>}
    </Paper>
  </Stack>
}
