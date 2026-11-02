import { Box, Button, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { DocumentLink } from '../../components/DocumentLink'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { ForbiddenState, RequestErrorState } from '../../components/RequestState'
import { currencyName, dateLabel, money } from './contracts'
import { PurchaseStatus } from './PurchaseStatus'
import { usePurchaseExpenses } from './queries'

export function PurchaseExpenses({ id, currencyIds }: { id: string; currencyIds: readonly string[] }) {
  const user = useCurrentUser().data
  const permitted = Boolean(user && hasAnyPermission(user, ['expense.view']))
  const expenses = usePurchaseExpenses(id, permitted)
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  const add = user && hasAnyPermission(user, ['expense.create']) ? <Button component={Link} to={`/expenses/new?purchaseInvoiceId=${id}`} variant="outlined" sx={{ mt: 2 }}>+ Додати витрату</Button> : null
  if (!permitted) return <Stack spacing={2}>{add}<ForbiddenState /></Stack>
  if (expenses.isPending) return <ListLoading label="Завантаження закупівельних витрат" />
  if (expenses.isError) return <RequestErrorState error={expenses.error} retry={() => { void expenses.refetch() }} />
  const rows = expenses.data
  const currencies = [...currencyIds, ...rows.map(expense => expense.currencyId)]
  return <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
    <Box sx={{ p: 2.5 }}><Typography variant="h2" component="h2">Закупівельні витрати</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Пов’язані витрати. Лише проведені документи впливають на поточну собівартість.</Typography>{add}</Box>
    {!rows.length ? <EmptyState icon="expense" title="Закупівельних витрат ще немає" description="Тут відображатимуться витрати, пов’язані з цією накладною." /> : mobile ? <Stack component="ul" aria-label="Закупівельні витрати" sx={{ listStyle: 'none', p: 0, m: 0 }}>{rows.map(expense => <Box component="li" key={expense.document.id} sx={{ p: 2.5, borderTop: '1px solid', borderColor: 'divider' }}><Stack direction="row" sx={{ justifyContent: 'space-between', gap: 1 }}><Typography variant="subtitle1"><DocumentLink id={expense.document.id} type="EXPENSE" number={expense.document.number} /></Typography><PurchaseStatus status={expense.document.status} /></Stack><Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>{dateLabel(expense.document.documentDate)}</Typography><Typography variant="subtitle1" sx={{ mt: 1.5 }}>{money(expense.amount)} {currencyName(expense.currencyId, currencies)}</Typography>{expense.document.comment && <Typography variant="body2" sx={{ mt: 1, overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{expense.document.comment}</Typography>}</Box>)}</Stack> : <TableContainer><Table aria-label="Закупівельні витрати" sx={{ minWidth: 730 }}><TableHead><TableRow><TableCell>Дата</TableCell><TableCell>Документ</TableCell><TableCell>Валюта</TableCell><TableCell align="right">Сума</TableCell><TableCell>Статус</TableCell><TableCell>Коментар</TableCell></TableRow></TableHead><TableBody>{rows.map(expense => <TableRow key={expense.document.id}><TableCell sx={{ whiteSpace: 'nowrap' }}>{dateLabel(expense.document.documentDate)}</TableCell><TableCell><DocumentLink id={expense.document.id} type="EXPENSE" number={expense.document.number} /></TableCell><TableCell>{currencyName(expense.currencyId, currencies)}</TableCell><TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>{money(expense.amount)}</TableCell><TableCell><PurchaseStatus status={expense.document.status} /></TableCell><TableCell sx={{ overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{expense.document.comment || '—'}</TableCell></TableRow>)}</TableBody></Table></TableContainer>}
  </Paper>
}
