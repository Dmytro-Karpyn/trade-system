import { render, screen } from '@testing-library/react'
import { ThemeProvider } from '@mui/material'
import { describe, expect, it } from 'vitest'
import { theme } from '../theme/theme'
import { tokens } from '../theme/tokens'
import { PurchaseStatus } from '../features/purchases/PurchaseStatus'
import { ActiveBadge } from './ActiveBadge'
import { StatusBadge } from './StatusBadge'
function luminance(hex: string) { const c = hex.replace('#', '').match(/../g)!.map(v => parseInt(v, 16) / 255).map(v => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4); return c[0] * .2126 + c[1] * .7152 + c[2] * .0722 }
function contrast(a: string, b: string) { const x = luminance(a), y = luminance(b); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05) }
describe('shared status system', () => {
  it('uses the same success style for documents and active entities', () => {
    render(<ThemeProvider theme={theme}><PurchaseStatus status="POSTED" /><ActiveBadge active /><StatusBadge status="DRAFT" /><StatusBadge status="PENDING" /><StatusBadge status="ERROR" /><ActiveBadge active={false} /></ThemeProvider>)
    const posted = screen.getByText('Проведено').closest('[data-status]')!
    const active = screen.getByText('Активний').closest('[data-status]')!
    expect(getComputedStyle(posted).backgroundColor).toBe(getComputedStyle(active).backgroundColor)
    expect(screen.getByText('Чернетка').closest('[data-status]')).toHaveAttribute('data-status', 'DRAFT')
    expect(screen.getByText('В обробці')).toBeVisible()
    expect(screen.getByText('Помилка')).toBeVisible()
    expect(screen.getByText('Неактивний')).toBeVisible()
  })
  it('keeps small status text and primary actions at AA contrast', () => {
    Object.values(tokens.statuses).forEach(tone => expect(contrast(tone.color, tone.background)).toBeGreaterThanOrEqual(4.5))
    expect(contrast(tokens.colors.actionInk, tokens.colors.primary)).toBeGreaterThanOrEqual(4.5)
    expect(contrast(tokens.colors.surface, tokens.colors.primaryHover)).toBeGreaterThanOrEqual(4.5)
    expect(contrast(tokens.colors.mutedInk, tokens.colors.background)).toBeGreaterThanOrEqual(4.5)
  })
})
