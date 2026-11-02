import { Box, Button, Dialog, DialogContent, DialogTitle, Link as MuiLink, Stack, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { DataTable } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { RequestErrorState } from '../../components/RequestState'
import { dateLabel } from '../purchases/contracts'
import { documentPermission, documentRoute, documentTypes } from './contracts'
import { useReservations, useStockMovements } from './queries'

export function StockDocumentLink({ id, type, number }: { id: string; type: string; number: string | null }) {
  const user = useCurrentUser().data, route = documentRoute(type, id)
  return route && user && hasAnyPermission(user, [documentPermission(type)]) ? <MuiLink component={Link} to={route}>{number || 'DRAFT'}</MuiLink> : <>{number || 'DRAFT'}</>
}
export interface StockScope { productId?: string; batchId?: string; storageLocationId?: string }
export function StockMovements({ scope }: { scope: StockScope }) {
  const query = useStockMovements(scope, true), mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  if (query.isPending) return <ListLoading label="Завантаження рухів" />
  if (query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  if (!query.data.length) return <EmptyState title="Рухів ще немає" description="Тут з’являться складські рухи проведених документів." />
  const quantity = (value: number) => <Typography variant="body2" sx={{ fontVariantNumeric: 'tabular-nums' }}>{value > 0 ? '+' : ''}{value}</Typography>
  return mobile ? <Box component="ul" aria-label="Рух товару" sx={{ m: 0, p: 0, listStyle: 'none' }}>{query.data.map(row => <MobileEntityCard key={row.id} title={<StockDocumentLink id={row.documentId} type={row.documentType} number={row.documentNumber} />} status={quantity(row.quantity)}><Typography variant="body2">{documentTypes[row.documentType] || row.documentType}</Typography><Typography variant="caption" color="text.secondary">{dateLabel(row.documentDate)} · {row.locationName || 'Склад без назви'}</Typography><Typography variant="body2">Баланс після руху: {row.physicalBalanceAfter}</Typography></MobileEntityCard>)}</Box> : <DataTable label="Рух товару" rows={query.data} rowId={row => row.id} columns={[
    { key: 'date', label: 'Дата', render: row => dateLabel(row.documentDate) },
    { key: 'document', label: 'Документ', render: row => <StockDocumentLink id={row.documentId} type={row.documentType} number={row.documentNumber} /> },
    { key: 'type', label: 'Тип операції', render: row => documentTypes[row.documentType] || row.documentType },
    { key: 'location', label: 'Місце зберігання', render: row => row.locationName || 'Склад без назви' },
    { key: 'quantity', label: 'Кількість', align: 'right', render: row => quantity(row.quantity) },
    { key: 'balance', label: 'Фізичний баланс після руху', align: 'right', render: row => row.physicalBalanceAfter },
  ]} />
}
export function StockReservations({ scope }: { scope: StockScope }) {
  const query = useReservations(scope, true)
  if (query.isPending) return <ListLoading label="Завантаження резервів" />
  if (query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  if (!query.data.length) return <EmptyState title="Активних резервів немає" />
  return <Stack spacing={1} sx={{ p: 2 }}>{query.data.map((row, index) => <Stack key={`${row.documentId}-${row.batchId}-${index}`} direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ py: 1, borderBottom: '1px solid', borderColor: 'divider', justifyContent: 'space-between' }}><Stack><Typography variant="body2">{documentTypes[row.documentType] || row.documentType} · <StockDocumentLink id={row.documentId} type={row.documentType} number={row.documentNumber} /></Typography><Typography variant="caption" color="text.secondary">{dateLabel(row.documentDate)}{row.createdByName ? ` · ${row.createdByName}` : ''}</Typography></Stack><Typography variant="body2">Резерв: {row.quantity}</Typography></Stack>)}</Stack>
}
export function ReservationDialog({ scope, close }: { scope: StockScope | null; close: () => void }) {
  return <Dialog open={Boolean(scope)} onClose={close} fullWidth maxWidth="md" aria-labelledby="reservation-dialog-title"><DialogTitle id="reservation-dialog-title">Джерела резерву</DialogTitle><DialogContent>{scope && <StockReservations scope={scope} />}<Button sx={{ mt: 2 }} onClick={close}>Закрити</Button></DialogContent></Dialog>
}
