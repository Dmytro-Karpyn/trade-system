import { useState } from 'react'
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { dateLabel, money } from '../purchases/contracts'
import { allocate } from './contracts'
import type { AvailableBatch } from './contracts'
import { SaleProductSelector } from './SaleProductSelector'
import type { SaleProduct } from './SaleProductSelector'
import { useAvailableBatches, useSalePrices } from './queries'

export function AllocationDialog({ locationId, saleId, priceTypeId, initialProduct, initialQuantity = 1, close, apply }: { locationId: string; saleId?: string; priceTypeId: string; initialProduct?: SaleProduct; initialQuantity?: number; close: () => void; apply: (product: SaleProduct, rows: { batch: AvailableBatch; quantity: number }[]) => void }) {
  const [product, setProduct] = useState<SaleProduct | null>(initialProduct ?? null); const [quantity, setQuantity] = useState(initialQuantity)
  const batches = useAvailableBatches(product?.id ?? '', locationId, saleId)
  const prices = useSalePrices(product ? [product.id] : [], priceTypeId)
  const result = allocate(batches.data ?? [], Number.isFinite(quantity) ? quantity : 0)
  const missingPrice = result.lines.some(line => !prices.options.get(product?.id ?? '')?.some(price => price.batchId === line.batch.batchId))
  const valid = product && quantity > 0 && Math.abs(quantity * 1e4 - Math.round(quantity * 1e4)) < 0.000001 && result.shortage === 0 && result.lines.length > 0 && !missingPrice && !batches.isFetching && !prices.pending
  return <Dialog open onClose={close} fullWidth maxWidth="sm"><DialogTitle>Підібрати партії</DialogTitle><DialogContent sx={{ pt: '12px !important' }}><Stack spacing={2.5}><SaleProductSelector label="Товар для підбору" value={product} change={setProduct} goodsOnly /><TextField label="Потрібна кількість" type="number" value={Number.isFinite(quantity) ? quantity : ''} onChange={event => setQuantity(Number(event.target.value))} slotProps={{ htmlInput: { min: '0.0001', step: '0.0001' } }} /><Typography variant="body2" color="text.secondary">Найстаріші доступні партії пропонуються першими. Наявні рядки цього товару буде замінено запропонованим розподілом.</Typography>
    {!batches.permitted ? <Alert severity="info">Потрібне право перегляду складу.</Alert> : product && batches.isPending ? <ListLoading label="Завантаження доступних партій" /> : batches.isError ? <RequestErrorState error={batches.error} retry={() => { void batches.refetch() }} /> : product && <>{result.shortage > 0 && <Alert severity="warning">Недостатньо доступного залишку. Не вистачає: {result.shortage}.</Alert>}{result.lines.map(line => <Box key={line.batch.batchId} sx={{ p: 2, border: '1px solid', borderColor: 'divider', borderRadius: 1 }}><Typography variant="subtitle1" sx={{ overflowWrap: 'anywhere' }}>{line.batch.batchNumber}</Typography><Typography variant="body2" color="text.secondary">{dateLabel(line.batch.purchaseDocumentDate)} · Кількість: {line.quantity}</Typography><Typography variant="body2">Рекомендована ціна: {prices.options.get(product.id)?.some(price => price.batchId === line.batch.batchId) ? `${money(prices.options.get(product.id)!.find(price => price.batchId === line.batch.batchId)!.unitPrice)} UAH` : 'Не задана'}</Typography></Box>)}{missingPrice && !prices.pending && <Alert severity="warning">Для частини партій не задано ціну вибраного типу.</Alert>}</>}
    {prices.error && <RequestErrorState error={prices.error} retry={() => { void prices.refetch() }} />}
  </Stack></DialogContent><DialogActions><Button onClick={close}>Скасувати</Button><Button variant="contained" disabled={!valid} onClick={() => { if (product) { apply(product, result.lines); close() } }}>Застосувати підбір</Button></DialogActions></Dialog>
}
