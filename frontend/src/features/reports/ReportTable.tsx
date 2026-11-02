import { Box, Button, Chip, Paper, Stack, Typography } from '@mui/material'
import { DataTable } from '../../components/DataTable'
import { KpiCard } from '../../components/KpiCard'
import { MoneyAmount } from '../../components/MoneyAmount'
import { BalanceBadge } from '../../components/BalanceBadge'
import { tokens } from '../../theme/tokens'
import { DocumentLink } from '../../components/DocumentLink'
import { EmptyState } from '../../components/EmptyState'
import { currencyName } from '../purchases/contracts'
import { cellText, metricText, csvText } from './format'
import { reportCurrencies } from './contracts'
import type { Metric, ReportTable as Data } from './contracts'

export function Metrics({ metrics, currencies }: { metrics: Metric[]; currencies: string[] }) {
  return (
    <Box
      aria-label="Підсумки"
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: 'repeat(2,minmax(0,1fr))', lg: 'repeat(4,minmax(0,1fr))' },
        gap: 1.5,
      }}
    >
      {metrics.map((metric, index) => (
        <KpiCard
          key={`${metric.key}-${metric.currencyId}-${index}`}
          label={
            metric.label +
            (metric.currencyId ? ' · ' + currencyName(metric.currencyId, currencies) : '')
          }
          value={metricText(metric, currencies)}
          testId={`metric-${metric.key}`}
        />
      ))}
    </Box>
  )
}
export function ReportDataTable({
  data,
  currencies = reportCurrencies(data),
  emptyTitle = 'Даних за вибраний період немає',
}: {
  data: Data
  currencies?: string[]
  emptyTitle?: string
}) {
  const rowIds = new Map(data.rows.map((row, index) => [row, String(index)]))
  return (
    <Paper variant="outlined">
      {!data.rows.length ? (
        <EmptyState title={emptyTitle} />
      ) : (
        <DataTable<Record<string, unknown>>
          label={data.title}
          rows={data.rows}
          rowId={(row) => rowIds.get(row)!}
          minWidth={data.columns.length > 5 ? 950 : 500}
          columns={data.columns.map((column) => ({
            key: column.key,
            label: column.label,
            align: ['money', 'number', 'percent'].includes(column.format)
              ? ('right' as const)
              : ('left' as const),
            render: (row) => (
              <Box
                sx={{
                  whiteSpace: ['money', 'number', 'date', 'percent'].includes(column.format)
                    ? 'nowrap'
                    : undefined,
                }}
              >
                {column.format === 'document' &&
                typeof row.documentId === 'string' &&
                typeof row.documentType === 'string' ? (
                  <DocumentLink
                    id={row.documentId}
                    type={row.documentType}
                    number={typeof row[column.key] === 'string' ? String(row[column.key]) : null}
                  />
                ) : column.format === 'purchase' && typeof row.purchaseInvoiceId === 'string' ? (
                  <DocumentLink
                    id={row.purchaseInvoiceId}
                    type="PURCHASE_INVOICE"
                    number={typeof row[column.key] === 'string' ? String(row[column.key]) : null}
                  />
                ) : column.key === 'state' && typeof row.balance === 'number' ? (
                  <BalanceBadge balance={row.balance} />
                ) : column.key === 'belowCost' && row[column.key] === true ? (
                  <Chip
                    size="small"
                    label="Нижче собівартості"
                    variant="outlined"
                    sx={{
                      backgroundColor: tokens.statuses.error.background,
                      color: tokens.statuses.error.color,
                      borderColor: tokens.statuses.error.border,
                    }}
                  />
                ) : column.format === 'money' && typeof row[column.key] === 'number' ? (
                  <MoneyAmount amount={row[column.key] as number} />
                ) : (
                  cellText(row[column.key], column.format, currencies)
                )}
              </Box>
            ),
          }))}
        />
      )}
    </Paper>
  )
}
export function CsvButton({
  data,
  filename,
  disabled = false,
}: {
  data?: Data
  filename: string
  disabled?: boolean
}) {
  const download = () => {
    if (!data) return
    const url = URL.createObjectURL(new Blob([csvText(data)], { type: 'text/csv;charset=utf-8' }))
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    a.click()
    URL.revokeObjectURL(url)
  }
  return (
    <Button variant="outlined" disabled={!data || disabled} onClick={download}>
      Експорт CSV
    </Button>
  )
}
export function ReportNotes({ notes }: { notes: string[] }) {
  return (
    <Stack spacing={1}>
      {notes.map((note) => (
        <Typography variant="caption" color="text.secondary" key={note}>
          {note}
        </Typography>
      ))}
    </Stack>
  )
}
