import { MoneyAmount } from './MoneyAmount'
export function CurrencyAmount({ amount, currency, signed = false }: { amount: number | null; currency: string; signed?: boolean }) {
  return <MoneyAmount amount={amount} currency={currency} signed={signed} />
}
