import type { ReactNode } from 'react'
import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material'

export interface DataColumn<Row> {
  key: string
  label: string
  render: (row: Row) => ReactNode
  align?: 'left' | 'right'
  width?: number
}
interface DataTableProps<Row> {
  label: string
  rows: readonly Row[]
  columns: DataColumn<Row>[]
  rowId: (row: Row) => string
  selectedId?: string | null
  select?: (row: Row) => void
  compact?: boolean
  minWidth?: number
}

// Data fetching, filtering and pagination belong to the feature's existing query.
export function DataTable<Row>({ label, rows, columns, rowId, selectedId, select, compact = false, minWidth }: DataTableProps<Row>) {
  return <TableContainer>
    <Table aria-label={label} size="small" sx={{ minWidth, ...(compact && { tableLayout: 'fixed', '& .MuiTableCell-root': { px: 1, py: 0.5 } }) }}>
      <TableHead><TableRow>{columns.map(column => <TableCell key={column.key} align={column.align} sx={{ width: column.width }}>{column.label}</TableCell>)}</TableRow></TableHead>
      <TableBody>{rows.map(row => <TableRow
        key={rowId(row)} hover selected={selectedId === rowId(row)}
        aria-selected={select ? selectedId === rowId(row) : undefined}
        tabIndex={select ? 0 : undefined}
        onClick={select ? event => {
          // Links/buttons retain their own actions and native modifier-click behavior.
          if (!(event.target as HTMLElement).closest('a,button,input,[role="button"]')) select(row)
        } : undefined}
        onKeyDown={select ? event => {
          if (event.target === event.currentTarget && ['Enter', ' '].includes(event.key)) {
            event.preventDefault()
            select(row)
          }
        } : undefined}
        sx={{ cursor: select ? 'pointer' : undefined, '&:focus-visible': { outlineOffset: -2 } }}
      >{columns.map(column => <TableCell key={column.key} align={column.align}>{column.render(row)}</TableCell>)}</TableRow>)}</TableBody>
    </Table>
  </TableContainer>
}
