import { RelatedReturnsSection as RelatedReturns } from '../../components/RelatedReturnsSection'
import { useState } from 'react'
import { Alert, Box, Button, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { EmptyState } from '../../components/EmptyState'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { PurchaseBatches, PurchasePrices } from './PurchaseBatches'
import { PurchaseForm } from './PurchaseForm'
import { PurchaseExpenses } from './PurchaseExpenses'
import { PurchaseInvoiceView } from './PurchaseInvoiceView'
import { PurchaseStatus } from './PurchaseStatus'
import { purchaseError } from './contracts'
import { usePurchaseNotice } from './notices'
import { usePurchase, usePurchaseBatches, usePurchaseLookups, usePurchaseMutations, usePurchaseSuppliers } from './queries'

const tabNames = ['Загальне', 'Товари', 'Партії', 'Витрати', 'Ціни']
export function PurchaseDetail({ id, embedded = false, onDirtyChange, onDeleted }: { id: string; embedded?: boolean; onDirtyChange?: (dirty: boolean) => void; onDeleted?: () => void }) {
  const user = useCurrentUser().data
  const purchase = usePurchase(id)
  const batches = usePurchaseBatches(id)
  const lookups = usePurchaseLookups()
  const suppliers = usePurchaseSuppliers()
  const mutations = usePurchaseMutations(id)
  const notify = usePurchaseNotice()
  const navigate = useNavigate()
  const [tab, setTab] = useState(0)
  const [formKey, setFormKey] = useState(0)
  const [confirm, setConfirm] = useState<'post' | 'unpost' | 'delete' | null>(null)
  const [error, setError] = useState<unknown>(null)
  const busy = mutations.transition.isPending || mutations.remove.isPending
  const refresh = async () => { await Promise.all([purchase.refetch(), batches.refetch()]); setFormKey(value => value + 1); setError(null) }
  if (!user) return null
  if (purchase.isPending) return <ListLoading label="Завантаження закупівлі" />
  if (purchase.isError) return purchase.error instanceof ApiError && purchase.error.status === 404 ? <EmptyState icon="purchase" title="Закупівлю не знайдено" action={<Button component={Link} to="/purchases">До закупівель</Button>} /> : <RequestErrorState error={purchase.error} retry={() => { void purchase.refetch() }} />
  const data = purchase.data
  if (data.document.documentType === 'OPENING_STOCK') return <Paper variant="outlined"><EmptyState icon="stock" title="Початкові залишки" description="UI початкових залишків буде реалізовано окремим етапом." /></Paper>
  const posted = data.document.status === 'POSTED'
  const canEdit = !posted && hasAnyPermission(user, ['purchase.create'])
  const canDelete = !posted && hasAnyPermission(user, ['purchase.delete'])
  const canPost = !posted && hasAnyPermission(user, ['purchase.post'])
  const canUnpost = posted && hasAnyPermission(user, ['purchase.unpost'])
  const supplier = suppliers.data?.find(item => item.id === data.counterpartyId)?.name
  const transition = async () => {
    if (!confirm) return
    setError(null)
    try {
      if (confirm === 'delete') { await mutations.remove.mutateAsync(); notify({ severity: 'success', message: 'Чернетку видалено.' }); if (onDeleted) onDeleted(); else void navigate('/purchases', { replace: true }) }
      else { await mutations.transition.mutateAsync({ action: confirm, documentId: id }); setTab(0); notify({ severity: 'success', message: confirm === 'post' ? 'Закупівлю проведено.' : 'Закупівлю повернуто в чернетку.' }) }
    } catch (caught) { setError(caught); notify({ severity: 'error', message: purchaseError(caught) }) }
    finally { setConfirm(null) }
  }
  const activeTab = !posted && tab > 1 ? 0 : tab
  return <Stack spacing={2}>
    <Box><Button component={Link} to={embedded ? `/purchases/${id}` : '/purchases'} color="inherit" startIcon={<Icon name={embedded ? 'upRight' : 'back'} />} sx={{ p: 0, mb: 1 }}>{embedded ? 'Відкрити окремо' : 'До закупівель'}</Button><PageHeader secondary={embedded} title={data.document.number || 'Чернетка закупівлі'} actions={<PurchaseStatus status={data.document.status} />} />{posted && data.document.postedAt && <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>Проведено {new Date(data.document.postedAt).toLocaleString('uk-UA')}{data.postedByName ? ` · ${data.postedByName}` : ''}</Typography>}</Box>
    {error != null && <Alert severity="error" action={<Button onClick={() => { void refresh() }} disabled={busy}>Оновити дані</Button>}>{purchaseError(error)}</Alert>}
    <Tabs value={activeTab} onChange={(_, value: number) => setTab(value)} variant="scrollable" scrollButtons="auto" allowScrollButtonsMobile aria-label="Розділи закупівлі" sx={{ borderBottom: '1px solid', borderColor: 'divider' }}>{tabNames.map((label, index) => <Tab label={label} disabled={!posted && index > 1} key={label} id={`purchase-${id}-tab-${index}`} aria-controls={`purchase-${id}-panel-${index}`} />)}</Tabs>
    <Box role="tabpanel" id={`purchase-${id}-panel-${activeTab}`} aria-labelledby={`purchase-${id}-tab-${activeTab}`}>
      {batches.isPending ? <ListLoading label="Завантаження партій закупівлі" /> : batches.isError ? <RequestErrorState error={batches.error} retry={() => { void batches.refetch() }} /> : !posted ? canEdit ? <PurchaseForm key={formKey} compact={embedded} linesOnly={activeTab === 1} onDirtyChange={onDirtyChange} purchase={data} batches={batches.data} supplierName={supplier} refresh={() => { void refresh() }} remove={canDelete ? () => setConfirm('delete') : undefined} saved={() => {}} /> : <PurchaseInvoiceView compact={embedded} linesOnly={activeTab === 1} purchase={data} lookups={lookups.data} supplierName={supplier} /> : activeTab <= 1 ? <Stack spacing={2}><PurchaseInvoiceView compact={embedded} linesOnly={activeTab === 1} purchase={data} lookups={lookups.data} supplierName={supplier} /><PurchaseBatches compact={embedded} purchase={data} batches={batches.data} canSeeCost={hasAnyPermission(user, ['cost.view'])} /></Stack> : activeTab === 2 ? <PurchaseBatches compact={embedded} purchase={data} batches={batches.data} canSeeCost={hasAnyPermission(user, ['cost.view'])} /> : activeTab === 3 ? <PurchaseExpenses id={id} currencyIds={lookups.data?.currencyIds ?? []} /> : <PurchasePrices compact={embedded} id={id} batches={batches.data} />}
    </Box>
    {posted && <RelatedReturns kind="supplier" sourceId={id} />}
    {!canEdit && (canDelete || canPost || canUnpost) && <DocumentActions danger={(canDelete || canUnpost) && <>{canDelete && <Button color="error" disabled={busy} onClick={() => setConfirm('delete')}>Видалити</Button>}{canUnpost && <Button color="error" variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>Розпровести</Button>}</>}>{canPost && <Button variant="contained" disabled={busy} onClick={() => setConfirm('post')}>Провести</Button>}</DocumentActions>}
    <ConfirmDialog open={Boolean(confirm)} busy={busy} danger={confirm !== 'post'} title={confirm === 'post' ? 'Провести закупівлю?' : confirm === 'unpost' ? 'Розпровести закупівлю?' : 'Видалити чернетку?'} action={confirm === 'post' ? 'Провести' : confirm === 'unpost' ? 'Розпровести' : 'Видалити'} close={() => setConfirm(null)} confirm={() => { void transition() }}>{confirm === 'post' ? 'Після проведення будуть створені партії та складські залишки.' : confirm === 'unpost' ? 'Документ повернеться в чернетку. Партії та їхні ціни буде збережено; складські рухи буде скасовано.' : 'Чернетку буде логічно видалено. Бізнес-дані залишаться в системі.'}</ConfirmDialog>
  </Stack>
}
export function PurchaseDetailPage() { const { id = '' } = useParams(); return <PurchaseDetail key={id} id={id} /> }
