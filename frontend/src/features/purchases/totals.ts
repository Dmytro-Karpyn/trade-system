// Display preview only. Persisted stock, costs and settlements are calculated by the backend.
// Integer scaling avoids rounding each line before the header discount.
const scale = (value: number, places: number) => BigInt((Number.isFinite(value) && value >= 0 && value < 1e16 ? value : 0).toFixed(places).replace('.', ''))
const roundDivide = (value: bigint, divisor: bigint) => (value + divisor / 2n) / divisor
export function purchaseTotals(lines: readonly { quantity: number; unitPriceCurrency: number }[], discountPercent: number, exchangeRate: number) {
  const gross = lines.reduce((sum, line) => sum + scale(line.quantity, 4) * scale(line.unitPriceCurrency, 4), 0n)
  const discount = scale(Math.min(100, Math.max(0, discountPercent)), 4)
  const net = roundDivide(gross * (1_000_000n - discount), 10_000_000_000n)
  return { gross: Number(gross) / 1e8, discount: Number(gross * discount) / 1e14, total: Number(net) / 1e4, uah: Number(roundDivide(net * scale(exchangeRate, 8), 100_000_000n)) / 1e4 }
}
