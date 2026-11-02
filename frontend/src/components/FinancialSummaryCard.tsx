import type { ReactNode } from 'react'
import { CurrencyAmount } from './CurrencyAmount'
import { KpiCard } from './KpiCard'
export function FinancialSummaryCard({ label, amount, currency, children, ariaLabel }: { label: string; amount: number | null; currency: string; children?: ReactNode; ariaLabel?: string }) {
  return <KpiCard label={label} value={<CurrencyAmount amount={amount} currency={currency} />} ariaLabel={ariaLabel}>{children}</KpiCard>
}
