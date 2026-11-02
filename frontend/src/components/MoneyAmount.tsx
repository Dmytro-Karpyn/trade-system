import { Box } from '@mui/material'
export function MoneyAmount({ amount, signed = false, currency = '' }: { amount: number | null; signed?: boolean; currency?: string }) {
  return <Box component="span" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{amount === null ? '—' : new Intl.NumberFormat('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 4, signDisplay: signed ? 'exceptZero' : 'auto' }).format(amount) + (currency ? ` ${currency}` : '')}</Box>
}
