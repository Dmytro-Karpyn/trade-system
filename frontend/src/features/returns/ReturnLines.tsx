import { Box, Chip, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography, useMediaQuery, useTheme } from '@mui/material'
import type { ReactNode } from 'react'
import { money } from '../purchases/contracts'
import type { ReturnableLine, ReturnKind, ReturnLookups } from './contracts'

export function ReturnLines({ kind, rows, lookups, inputs, refunds, readonlyQuantities, compact = false }: { compact?: boolean; kind: ReturnKind; rows: ReturnableLine[]; lookups?: ReturnLookups; inputs?: (row: ReturnableLine, index: number) => ReactNode; refunds?: Map<string, number>; readonlyQuantities?: Map<string, number> }) {
  const narrow = useMediaQuery(useTheme().breakpoints.down('md'))
  const mobile = compact || narrow
  const name = (row: ReturnableLine) => lookups?.products.find(p => p.productId === row.productId)?.name || 'Номенклатура'
  const metrics = (row: ReturnableLine) => {
    const values: [string, number][] = kind === 'customer' ? [['Продано', row.originalQuantity], ['Уже повернено', row.alreadyReturnedQuantity], ['Доступно до повернення', row.returnableQuantity]]
      : [['Закуплено', row.originalQuantity], ['Уже повернено', row.alreadyReturnedQuantity], ['Залишок за закупівлею', row.returnableQuantity], ['Фізичний залишок', row.physicalQuantity ?? 0], ['У резерві', row.reservedQuantity ?? 0], ['Доступний залишок', row.availableQuantity ?? 0], ['Доступно до власного резерву', row.availableForDocument ?? 0], ['Максимум до повернення', row.maxReturnableQuantity ?? 0]]
    values.push(['Початкова ціна', row.originalUnitPrice], ['Знижка, %', row.originalDiscountPercent]); if (row.refundableAmount !== undefined) values.push(['Залишок суми компенсації', row.refundableAmount])
    return <Box component="dl" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', lg: 'repeat(3, 1fr)' }, gap: 1.5, m: 0 }}>{values.map(([label, value]) => <Box key={label}><Typography component="dt" variant="caption" color="text.secondary">{label}</Typography><Typography component="dd" variant="body2" sx={{ m: 0 }}>{money(value)}</Typography></Box>)}</Box>
  }
  const product = (row: ReturnableLine) => <Stack spacing={1}><Typography variant="subtitle1">{name(row)}</Typography><Box><Chip size="small" label={row.itemType === 'GOODS' ? 'Товар' : 'Послуга'} variant="outlined" /></Box>{row.itemType === 'GOODS' && <Typography variant="body2" color="text.secondary">Партія: {lookups?.batches.find(b => b.batchId === row.batchId)?.batchNumber || 'Початкова партія'}</Typography>}{row.itemType === 'SERVICE' && <Typography variant="caption" color="text.secondary">Для послуги можна вказати часткову суму компенсації.</Typography>}</Stack>
  const controls = (row: ReturnableLine, index: number) => inputs ? inputs(row, index) : <Stack spacing={1}><Typography variant="body2">Кількість: {money(readonlyQuantities?.get(row.sourceLineId) ?? 0)}</Typography><Typography variant="subtitle1">Компенсація: {money(refunds?.get(row.sourceLineId) ?? 0)}</Typography></Stack>
  return mobile ? <Stack component="ul" aria-label="Рядки повернення" sx={{ listStyle: 'none', p: 0, m: 0 }} spacing={2}>{rows.map((row, i) => <Paper component="li" variant="outlined" key={row.sourceLineId} sx={{ p: 2 }}><Stack spacing={2}>{product(row)}{metrics(row)}{controls(row, i)}</Stack></Paper>)}</Stack>
    : <TableContainer component={Paper} variant="outlined"><Table aria-label="Рядки повернення" sx={{ minWidth: 900 }}><TableHead><TableRow><TableCell>Номенклатура / партія</TableCell><TableCell>Початкові дані та ліміти</TableCell><TableCell sx={{ minWidth: 230 }}>Кількість / компенсація</TableCell></TableRow></TableHead><TableBody>{rows.map((row, i) => <TableRow key={row.sourceLineId} sx={{ verticalAlign: 'top' }}><TableCell sx={{ width: '24%' }}>{product(row)}</TableCell><TableCell>{metrics(row)}</TableCell><TableCell>{controls(row, i)}</TableCell></TableRow>)}</TableBody></Table></TableContainer>
}
