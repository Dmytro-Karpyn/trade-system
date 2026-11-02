import { Divider, Paper, Stack, Typography } from '@mui/material'
import { money } from '../purchases/contracts'
import { totals } from './contracts'
export function SaleTotals({ lines, discount }: { lines: { quantity: number; unitPrice: number }[]; discount: number }) {
  const result = totals(lines, discount)
  return <Paper component="section" aria-label="Підсумки продажу" variant="outlined" sx={{ p: 2.5, width: '100%', maxWidth: 500, alignSelf: 'flex-end' }}><Stack spacing={1.5}><Typography variant="h2" component="h2">Підсумки</Typography>{[['Сума товарів/послуг', money(result.gross) + ' UAH'], ['Знижка', `${Number.isFinite(discount) ? discount : 0}%`], ['Сума знижки', money(result.discount) + ' UAH']].map(([label, value]) => <Stack direction="row" key={label} sx={{ justifyContent: 'space-between', gap: 2 }}><Typography variant="body2" color="text.secondary">{label}</Typography><Typography variant="body2">{value}</Typography></Stack>)}<Divider /><Stack direction="row" sx={{ justifyContent: 'space-between', gap: 2 }}><Typography variant="subtitle1">До сплати</Typography><Typography variant="subtitle1">{money(result.total)} UAH</Typography></Stack></Stack></Paper>
}
