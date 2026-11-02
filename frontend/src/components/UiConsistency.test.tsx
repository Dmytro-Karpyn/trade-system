import { CssBaseline, ThemeProvider } from '@mui/material'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { theme } from '../theme/theme'
import { tokens } from '../theme/tokens'
import { ConfirmDialog } from './ConfirmDialog'
import { DataTable } from './DataTable'

describe('UI consistency regressions', () => {
  it('marks a selected row structurally and exposes keyboard selection', async () => {
    const select = vi.fn()
    render(<ThemeProvider theme={theme}><CssBaseline /><DataTable label="Документи" rows={[{ id: 'one', name: 'Документ' }]} columns={[{ key: 'name', label: 'Назва', render: row => row.name }]} rowId={row => row.id} selectedId="one" select={select} /></ThemeProvider>)
    const row = screen.getByRole('row', { name: 'Документ' })
    expect(row).toHaveAttribute('aria-selected', 'true')
    expect(getComputedStyle(within(row).getByRole('cell')).boxShadow).toContain('inset 3px')
    row.focus(); await userEvent.keyboard('{Enter}')
    expect(select).toHaveBeenCalledWith({ id: 'one', name: 'Документ' })
  })
  it('uses the warm overlay shadow and labeled confirmation actions', () => {
    render(<ThemeProvider theme={theme}><ConfirmDialog open title="Видалити чернетку?" action="Видалити" danger close={() => {}} confirm={() => {}}>Бізнес-дані зберігаються.</ConfirmDialog></ThemeProvider>)
    const dialog = screen.getByRole('dialog', { name: 'Видалити чернетку?' })
    expect(dialog).toHaveStyle({ boxShadow: tokens.overlayShadow })
    expect(within(dialog).getByRole('button', { name: 'Видалити' })).toBeEnabled()
  })
})
