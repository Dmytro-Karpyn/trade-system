import { Box, Button, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { BalanceBadge } from '../../components/BalanceBadge'
import { FinancialSummaryCard } from '../../components/FinancialSummaryCard'
import { MoneyAmount } from '../../components/MoneyAmount'
import { DocumentLink } from '../../components/DocumentLink'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { operationNames, uahId } from './contracts'
import { usePartySettlements } from './queries'

export function SettlementsTab({ id, showDetailLink = true, currencyIds, compact = false }: { compact?: boolean; id: string; showDetailLink?: boolean; currencyIds?: string[] }) {
  const query = usePartySettlements(id)
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (query.isPending) return <ListLoading label="Завантаження взаєморозрахунків" />
  if (query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const { balances, movements } = query.data
  // Currency directory is not implemented. Only the agreed UAH ID has a known label;
  // other currencies get distinct neutral labels, never an invented ISO code or a visible GUID.
  const unknownIds = [...new Set([...(currencyIds ?? []), ...[...balances, ...movements].map(item => item.currencyId)])].filter(currency => currency !== uahId).sort()
  const currencyName = (currency: string) => currency === uahId ? 'UAH' : `Валюта ${unknownIds.indexOf(currency) + 1}`
  const date = (value: string) => value.split('-').reverse().join('.')
  return <Stack spacing={2}>
    <Box><Typography variant="h2" component="h2">Баланси за валютами</Typography>{showDetailLink && <Button component={Link} to={`/settlements/${id}`} sx={{ p: 0, mt: 1 }}>Відкрити взаєморозрахунки контрагента</Button>}<Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Взаєморозрахунки за проведеними документами</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>+ = контрагент винен нам; − = ми винні контрагенту.</Typography></Box>
    {balances.length > 0 && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' }, gap: 2 }}>{balances.map(balance => <FinancialSummaryCard key={balance.currencyId} ariaLabel={`Баланс ${currencyName(balance.currencyId)}`} label={currencyName(balance.currencyId)} amount={balance.balance} currency="">{balance.currencyId !== uahId && <Typography variant="caption" color="text.secondary">Назву валюти не задано</Typography>}<Box sx={{ mt: 0.5 }}><BalanceBadge balance={balance.balance} /></Box></FinancialSummaryCard>)}</Box>}
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <Box sx={{ p: 2 }}><Typography component="h2" variant="h2">Рухи взаєморозрахунків</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>У хронологічному порядку, баланс окремо для кожної валюти</Typography></Box>
      {!movements.length ? <EmptyState icon="settlement" title="Рухів взаєморозрахунків ще немає" description="Тут відображатимуться проведені операції з цим контрагентом." /> : mobile ? <Box component="ul" aria-label="Рухи взаєморозрахунків" sx={{ p: 0, m: 0, listStyle: 'none' }}>{movements.map(movement => <Box component="li" key={movement.id} sx={{ p: 2, borderTop: '1px solid', borderColor: 'divider' }}><Stack direction="row" sx={{ justifyContent: 'space-between', gap: 2 }}><Typography variant="subtitle1" sx={{ overflowWrap: 'anywhere' }}><DocumentLink id={movement.documentId} type={movement.documentType} number={movement.documentNumber} /></Typography><Typography variant="caption" color="text.secondary">{date(movement.documentDate)}</Typography></Stack><Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{operationNames[movement.documentType] ?? 'Інша операція'}</Typography><Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}><Box><Typography variant="caption" color="text.secondary">Рух · {currencyName(movement.currencyId)}</Typography><Typography variant="body2" sx={{ fontWeight: 600 }}><MoneyAmount amount={movement.amount} signed /></Typography></Box><Box><Typography variant="caption" color="text.secondary">Баланс після операції</Typography><Typography variant="body2" sx={{ fontWeight: 600 }}><MoneyAmount amount={movement.balanceAfterMovement} /></Typography></Box></Box></Box>)}</Box> : <TableContainer><Table aria-label="Рухи взаєморозрахунків" sx={{ minWidth: compact ? 600 : 760 }}><TableHead><TableRow><TableCell>Дата</TableCell><TableCell>Документ</TableCell><TableCell>Тип операції</TableCell><TableCell>Валюта</TableCell><TableCell align="right">Рух</TableCell><TableCell align="right">Баланс після операції</TableCell></TableRow></TableHead><TableBody>{movements.map(movement => <TableRow key={movement.id} hover><TableCell sx={{ whiteSpace: 'nowrap' }}>{date(movement.documentDate)}</TableCell><TableCell sx={{ fontWeight: 550 }}><DocumentLink id={movement.documentId} type={movement.documentType} number={movement.documentNumber} /></TableCell><TableCell>{operationNames[movement.documentType] ?? 'Інша операція'}</TableCell><TableCell>{currencyName(movement.currencyId)}</TableCell><TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}><MoneyAmount amount={movement.amount} signed /></TableCell><TableCell align="right" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}><MoneyAmount amount={movement.balanceAfterMovement} /></TableCell></TableRow>)}</TableBody></Table></TableContainer>}
    </Paper>
  </Stack>
}
