import { RelatedReturnsSection as RelatedReturns } from '../../components/RelatedReturnsSection'
import { useState } from 'react'
import { Alert, Box, Button, Stack, Tab, Tabs, Typography } from '@mui/material'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { usePriceTypes } from '../counterparties/queries'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { CustomerBalance } from './CustomerBalance'
import { saleError } from './contracts'
import { SaleForm } from './SaleForm'
import { SaleInvoiceView } from './SaleInvoiceView'
import { SaleProfit } from './SaleProfit'
import { useSaleNotice } from './notices'
import { useCustomers, useSale, useSaleLookups, useSaleMutations } from './queries'

export function SaleDetail({ id, embedded = false, onDeleted, onDirtyChange }: { id: string; embedded?: boolean; onDeleted?: () => void; onDirtyChange?: (dirty: boolean) => void }) {
  const user = useCurrentUser().data; const sale = useSale(id); const lookups = useSaleLookups(); const customers = useCustomers(); const types = usePriceTypes()
  const mutations = useSaleMutations(id); const notify = useSaleNotice(); const navigate = useNavigate()
  const [formKey, setFormKey] = useState(0); const [confirm, setConfirm] = useState<'post' | 'unpost' | 'delete' | null>(null); const [error, setError] = useState<unknown>(null)
  const [tab, setTab] = useState('document')
  const busy = mutations.transition.isPending || mutations.remove.isPending
  const refresh = async () => { await sale.refetch(); setFormKey(value => value + 1); setError(null) }
  if (!user) return null
  if (sale.isPending) return <ListLoading label="Завантаження продажу" />
  if (sale.isError) return sale.error instanceof ApiError && sale.error.status === 404 ? <EmptyState icon="sale" title="Продаж не знайдено" action={<Button component={Link} to="/sales">До продажів</Button>} /> : <RequestErrorState error={sale.error} retry={() => { void sale.refetch() }} />
  const data = sale.data; const posted = data.document.status === 'POSTED'
  const activeTab = posted ? tab : 'document'
  const canEdit = !posted && hasAnyPermission(user, ['sale.create']); const canPost = !posted && hasAnyPermission(user, ['sale.post']); const canDelete = !posted && hasAnyPermission(user, ['sale.delete']); const canUnpost = posted && hasAnyPermission(user, ['sale.unpost'])
  const customer = customers.data?.find(customer => customer.id === data.counterpartyId)
  const perform = async () => {
    if (!confirm) return
    setError(null)
    try {
      if (confirm === 'delete') { await mutations.remove.mutateAsync(); notify({ severity: 'success', message: 'Чернетку продажу видалено.' }); if (onDeleted) onDeleted(); else void navigate('/sales', { replace: true }) }
      else { await mutations.transition.mutateAsync({ action: confirm, documentId: id }); notify({ severity: 'success', message: confirm === 'post' ? 'Продаж проведено.' : 'Продаж повернуто в чернетку.' }) }
    } catch (caught) { setError(caught); notify({ severity: 'error', message: saleError(caught) }) }
    finally { setConfirm(null) }
  }
  const actions = <DocumentActions danger={(!canEdit && canDelete || canUnpost) ? <>{!canEdit && canDelete && <Button color="error" disabled={busy} onClick={() => setConfirm('delete')}>Видалити</Button>}{canUnpost && <Button color="error" variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>{busy ? 'Розпроводимо…' : 'Розпровести'}</Button>}</> : undefined}>{!canEdit && canPost && <Button variant="contained" disabled={busy} onClick={() => setConfirm('post')}>Провести</Button>}</DocumentActions>
  return <Stack spacing={2}><Box><Button component={Link} to={embedded ? `/sales/${id}` : '/sales'} color="inherit" startIcon={<Icon name={embedded ? 'upRight' : 'back'} />} sx={{ p: 0, mb: 1 }}>{embedded ? 'Відкрити окремо' : 'До продажів'}</Button><PageHeader secondary={embedded} title={data.document.number || 'Чернетка продажу'} actions={<PurchaseStatus status={data.document.status} />} />{posted && data.document.postedAt && <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>Проведено {new Date(data.document.postedAt).toLocaleString('uk-UA')}{data.postedByName ? ` · ${data.postedByName}` : ''}</Typography>}</Box>
    <Tabs value={activeTab} onChange={(_, value: string) => setTab(value)} aria-label="Вкладки продажу" variant="scrollable" sx={{ borderBottom: '1px solid', borderColor: 'divider', minHeight: 44 }}><Tab value="document" label="Документ" id={`sale-${id}-document-tab`} aria-controls={`sale-${id}-document`} /><Tab value="returns" label="Повернення" disabled={!posted} id={`sale-${id}-returns-tab`} aria-controls={`sale-${id}-returns`} /></Tabs>
    <Box role="tabpanel" id={`sale-${id}-document`} aria-labelledby={`sale-${id}-document-tab`} hidden={activeTab !== 'document'}><Stack spacing={2}>
    {error != null && <Alert severity="error" action={<Button disabled={busy} onClick={() => { void refresh() }}>Оновити дані</Button>}>{saleError(error)}</Alert>}
    {canEdit ? <SaleForm key={formKey} compact={embedded} onDirtyChange={onDirtyChange} sale={data} saved={() => {}} refresh={() => { void refresh() }} remove={canDelete ? () => setConfirm('delete') : undefined} /> : <><SaleInvoiceView compact={embedded} sale={data} locations={lookups.data?.locations ?? []} buyerName={customer?.name} priceTypeName={types.data?.find(type => type.id === data.priceTypeId)?.name} />{customer && !customer.isSystem && <CustomerBalance id={customer.id} />}</>}
    {posted && <SaleProfit id={id} />}
    {posted && activeTab === 'document' && <RelatedReturns kind="customer" sourceId={id} />}
    </Stack></Box><Box role="tabpanel" id={`sale-${id}-returns`} aria-labelledby={`sale-${id}-returns-tab`} hidden={activeTab !== 'returns'}>{posted && activeTab === 'returns' && <RelatedReturns kind="customer" sourceId={id} />}</Box>
    {!canEdit && (canPost || canUnpost || canDelete) && actions}
    <ConfirmDialog open={Boolean(confirm)} title={confirm === 'post' ? 'Провести продаж?' : confirm === 'unpost' ? 'Розпровести продаж?' : 'Видалити чернетку продажу?'} action={confirm === 'post' ? 'Провести' : confirm === 'unpost' ? 'Розпровести' : 'Видалити'} busy={busy} danger={confirm !== 'post'} close={() => setConfirm(null)} confirm={() => { void perform() }}>{confirm === 'post' ? 'Після проведення буде списано товар зі складу та створено взаєморозрахунок з покупцем.' : confirm === 'unpost' ? 'Складські рухи та взаєморозрахунок документа буде скасовано. Чернетка знову резервуватиме товар.' : 'Документ буде логічно видалено, його резерв звільниться.'}</ConfirmDialog>
  </Stack>
}
export function SaleDetailPage() { const { id = '' } = useParams(); return <SaleDetail key={id} id={id} /> }
