import { Box, Button, Chip, Link as MuiLink, Stack, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { useState } from 'react'
import { DataTable } from '../../components/DataTable'
import type { DataColumn } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { priceRange, stockRowId } from './contracts'
import type { StockRow } from './contracts'
import { ReservationDialog } from './StockHistory'
import type { StockScope } from './StockHistory'

export function StockTable({ rows, canCost, batches = false, showPrices = true, compact = false, selectedId, select }: {
  rows: StockRow[]; canCost: boolean; batches?: boolean; showPrices?: boolean; compact?: boolean; selectedId?: string | null; select?: (row: StockRow) => void
}) {
  const mobile = useMediaQuery(useTheme().breakpoints.down('md')), [scope, setScope] = useState<StockScope | null>(null)
  if (!rows.length) return <EmptyState title="Залишків немає" description="Змініть фільтри або проведіть складський документ." />
  const reserved = (row: StockRow) => <Button size="small" aria-label={`Резерв ${row.productName}${row.batchNumber ? ` ${row.batchNumber}` : ''}`} onClick={() => setScope({ productId: row.productId, batchId: row.batchId ?? undefined, storageLocationId: row.storageLocationId })} sx={{ p: 0, minWidth: 0 }}>{row.reservedQuantity}</Button>
  const name = (row: StockRow) => <MuiLink component={Link} to={batches && row.batchId ? `/stock/batches/${row.batchId}` : `/stock/products/${row.productId}`} onClick={event => { if (select && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); select(row) } }} title={(batches ? row.batchNumber : row.productName) ?? undefined} sx={{ display: 'block', maxWidth: mobile ? '100%' : batches ? 200 : 220, whiteSpace: mobile ? 'normal' : 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', overflowWrap: 'anywhere' }}>{batches ? row.batchNumber : row.productName}</MuiLink>
  const columns: DataColumn<StockRow>[] = [
    { key: 'name', label: batches ? 'Партія' : 'Товар', width: batches ? 200 : 180, render: name },
    ...(batches ? [
      { key: 'product', label: 'Товар', width: 160, render: (row: StockRow) => <Box title={row.productName} sx={{ maxWidth: 160, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{row.productName}</Box> },
      ...(!compact ? [{ key: 'date', label: 'Дата закупівлі', render: (row: StockRow) => row.purchaseDocumentDate ? new Date(row.purchaseDocumentDate + 'T00:00:00').toLocaleDateString('uk-UA') : '—' }, { key: 'supplier', label: 'Постачальник', render: (row: StockRow) => row.supplier || '—' }] : []),
    ] : !compact ? [{ key: 'code', label: 'Код', render: (row: StockRow) => row.code }, { key: 'category', label: 'Категорія', render: (row: StockRow) => row.category || (row.categoryId ? 'Назву не задано' : '—') }] : []),
    { key: 'location', label: 'Місце зберігання', width: 150, render: row => <Box title={row.locationName || undefined} sx={{ maxWidth: 150, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{row.locationName || 'Склад без назви'}</Box> },
    { key: 'physical', label: 'Фізичний залишок', align: 'right', render: row => row.physicalQuantity },
    { key: 'reserved', label: 'Резерв', align: 'right', render: reserved },
    { key: 'available', label: 'Доступно', align: 'right', render: row => row.availableQuantity },
    ...(!compact ? [{ key: 'unit', label: 'Одиниця', render: (row: StockRow) => row.unit || '—' }] : []),
    ...(showPrices ? [{ key: 'retail', label: 'Роздрібна ціна', render: (row: StockRow) => priceRange(row.retailPrice) }, { key: 'wholesale', label: 'Оптова ціна', render: (row: StockRow) => priceRange(row.wholesalePrice) }] : []),
    ...(canCost ? [{ key: 'cost', label: 'Собівартість', render: (row: StockRow) => priceRange(row.currentUnitCost) }] : []),
  ]
  return <>{mobile ? <Box component="ul" aria-label="Складські залишки" sx={{ listStyle: 'none', p: 0, m: 0 }}>{rows.map(row => <MobileEntityCard key={stockRowId(row)} title={name(row)} status={<Typography variant="caption" color="text.secondary">{row.unit || '—'}</Typography>}><Stack spacing={1}>{batches && <Typography>{row.productName}</Typography>}<Typography variant="body2" color="text.secondary">{row.code} · {row.locationName || 'Склад без назви'}</Typography><Stack direction="row" spacing={1.5} sx={{ flexWrap: 'wrap', rowGap: 1 }}><Chip size="small" label={`Фізично: ${row.physicalQuantity}`} /><Box>Резерв: {reserved(row)}</Box><Box>Доступно: {row.availableQuantity}</Box></Stack>{showPrices && <Typography variant="body2">Роздрібна: {priceRange(row.retailPrice)} · Оптова: {priceRange(row.wholesalePrice)}</Typography>}{canCost && <Typography variant="body2">Собівартість: {priceRange(row.currentUnitCost)} UAH</Typography>}</Stack></MobileEntityCard>)}</Box> : <DataTable label="Складські залишки" minWidth={compact ? showPrices || canCost ? 1000 : 700 : batches ? 1100 : 1000} rows={rows} columns={columns} rowId={stockRowId} selectedId={selectedId} select={select} />}<ReservationDialog scope={scope} close={() => setScope(null)} /></>
}
