import { render, screen } from '@testing-library/react'
import { ThemeProvider } from '@mui/material'
import { describe, expect, it } from 'vitest'
import { theme } from '../theme/theme'
import { BalanceBadge } from './BalanceBadge'
import { CurrencyAmount } from './CurrencyAmount'
import { FinancialSummaryCard } from './FinancialSummaryCard'

describe('financial visuals', () => {
  it('keeps balance signs separate from success/error semantics', () => {
    render(<ThemeProvider theme={theme}><BalanceBadge balance={50} /><BalanceBadge balance={-50} /><BalanceBadge balance={0} /></ThemeProvider>)
    const positive = screen.getByText('Нам винні').closest('.MuiChip-root')!, negative = screen.getByText('Ми винні').closest('.MuiChip-root')!
    expect(getComputedStyle(positive).color).toBe(getComputedStyle(negative).color)
    expect(screen.getByText('Нульовий баланс')).toBeVisible()
  })
  it('formats signed movements, preserves negative money and does not invent a missing amount', () => {
    render(<ThemeProvider theme={theme}><CurrencyAmount amount={50} currency="UAH" signed /><FinancialSummaryCard label="Каса" amount={-50} currency="UAH" /><CurrencyAmount amount={null} currency="UAH" /></ThemeProvider>)
    expect(screen.getByText('+50,00 UAH')).toBeVisible()
    expect(screen.getByText('-50,00 UAH')).toBeVisible()
    expect(screen.getByText('—')).toBeVisible()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
